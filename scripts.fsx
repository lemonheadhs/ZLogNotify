#load ".paket/load/net472/scripts/scripts.group.fsx"

open System
open FSharp.Azure.StorageTypeProvider
open FSharp.Data

System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

type Local = AzureTypeProvider<"UseDevelopmentStorage=true", autoRefresh = 5>

Local.Tables.ZLAuthnAttempts |> ignore
Local.Tables.ZLTokens |> ignore    

let pole = DateTime(2030, 1, 1).Ticks
let validPoint = (pole - DateTimeOffset.Now.UtcTicks).ToString().PadLeft(16, '0')

Local.Tables.ZLTokens.Query()
    .``Where Partition Key Is``.``Equal To``("ZohoTokens")
    //.``Where Row Key Is``.``Less Than``(validPoint)
    .Execute(1)

3417699477254277L > (pole - DateTimeOffset.Now.UtcTicks)

type ProjLogs = JsonProvider<"./samples/projectLogsSample.json", ResolutionFolder = __SOURCE_DIRECTORY__>

let logs = ProjLogs.GetSample()

let fst = logs.Timelogs.Date.[0]

fst.Tasklogs.[0]

