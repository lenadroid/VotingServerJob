#r "../packages/FAKE/tools/FakeLib.dll"
#r "../packages/Suave/lib/net40/Suave.dll"
#r "../packages/Dynamitey/lib/net40/Dynamitey.dll"
#r "../packages/Microsoft.AspNet.SignalR.Core/lib/net45/Microsoft.AspNet.SignalR.Core.dll"
#r "../packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "../packages/Microsoft.AspNet.Cors/lib/net45/System.Web.Cors.dll"
#r "../packages/Microsoft.Owin/lib/net45/Microsoft.Owin.dll"
#r "../packages/Microsoft.Owin.Hosting/lib/net45/Microsoft.Owin.Hosting.dll"
#r "../packages/Microsoft.Owin.Diagnostics/lib/net45/Microsoft.Owin.Diagnostics.dll"
#r "../packages/Microsoft.Owin.Host.HttpListener/lib/net45/Microsoft.Owin.Host.HttpListener.dll"
#r "../packages/Microsoft.Owin.Security/lib/net45/Microsoft.Owin.Security.dll"
#r "../packages/Microsoft.Owin.Cors/lib/net45/Microsoft.Owin.Cors.dll"
#r "../packages/FSharp.Dynamic/lib/net40/FSharp.Dynamic.dll"
#r "../packages/Owin/lib/net40/Owin.dll"

open Fake
open Suave
open Suave.Http.Successful
open Suave.Web
open Suave.Types
open System.Net

open System
open Microsoft.AspNet.SignalR
open Microsoft.AspNet.SignalR.Hubs
open Microsoft.Owin.Hosting
open Microsoft.AspNet.SignalR.Owin
open EkonBenefits.FSharp.Dynamic

type VoteId = {
    mutable ConnectionId: string; 
    mutable VotingRoom: string;
}

type VoteValue = {
    mutable PercentOfAgree: int 
    mutable Time: DateTime 
}

type VoteResult = {
    mutable VotingRoom : string;
    mutable PercentOfAgree : float;
    mutable Time: DateTime 
}

type VotingService = class
    val mutable VoteMap : Map<VoteId,VoteValue>
    val mutable RoomResults : List<VoteResult>
    new () = { VoteMap = Map.empty; RoomResults = List.empty }
  end

type public Startup() = 
    member public this.Configuration(app) = 
        let config = new HubConfiguration()
        config.EnableDetailedErrors <- true
        Owin.MapExtensions.Map(app, "/signalr", 
                               fun map -> 
                                   Owin.CorsExtensions.UseCors(map, Microsoft.Owin.Cors.CorsOptions.AllowAll) |> ignore
                                   Owin.OwinExtensions.RunSignalR(map, config))
        |> ignore

let votingService = new VotingService()

[<HubName("voteHub")>]
type public VoteHub() as this = 
    inherit Hub()
    member public x.Vote(room: string, percent: int) = 
        let key = { ConnectionId = this.Context.ConnectionId; VotingRoom = room }
        match (votingService.VoteMap |> Map.tryFind key) with
        | Some(value) -> 
            votingService.VoteMap <- (Map.remove key votingService.VoteMap)
            printfn "Updated vote of  %A to percentage from %A to %A" key.ConnectionId value.PercentOfAgree percent
        | None        -> printfn "Member %A haven't voted at this room before. His vote is %A" key.ConnectionId percent
        votingService.VoteMap <- votingService.VoteMap.Add(key,{PercentOfAgree = percent;Time = DateTime.Now})
        
        let averageRoomVote = votingService.VoteMap 
                              |> Map.filter (fun k v -> k.VotingRoom.Equals(room))
                              |> Map.toList |> List.map ( fun (k, v) -> double(v.PercentOfAgree))
                              |> List.average 
        printfn "\n New average is: %A \n" averageRoomVote
        votingService.RoomResults <- votingService.RoomResults |> List.append [{VotingRoom = room; Time=DateTime.Now; PercentOfAgree = averageRoomVote}]
        votingService.RoomResults |> List.iter ( fun x -> printfn "%A" x)
        //this.Clients.All?addMessage(room, votingService.RoomResults.Head.PercentOfAgree)
        this.Clients.Group(room)?addMessage(room, votingService.RoomResults.Head.PercentOfAgree)
        |> ignore
    member public x.ClosePoll(room: string) = 
        this.Clients.Group(room)?addMessage(room, votingService.RoomResults.Head.PercentOfAgree, "disconnect")
        |> ignore
    member public x.JoinRoom(room: string) =
        let previousVotes = votingService.RoomResults |> List.filter (fun v -> v.VotingRoom = room) |> List.rev
        if (previousVotes.Length > 0 && previousVotes.Length < 15) 
        then previousVotes |> List.iter (fun v -> this.Clients.Client(this.Context.ConnectionId)?addMessage(v.VotingRoom, v.PercentOfAgree))
        this.Groups.Add(this.Context.ConnectionId, room)
    member public x.LeaveRoom(room: string) =
        this.Groups.Remove(this.Context.ConnectionId, room)

let mainFunc = 
    // start up of the server
    let url = "http://localhost:8888/"
    use webApp = WebApp.Start<Startup>(url)
    Console.ForegroundColor = ConsoleColor.Green |> ignore
    Console.WriteLine("Server running on {0}", url)

    // start generating server side data to send to connected clients
    let context : IHubContext = GlobalHost.ConnectionManager.GetHubContext<VoteHub>()
    //generateDataUpdates context
    Console.ReadLine() |> ignore

mainFunc
