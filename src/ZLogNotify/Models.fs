module ZLogNotify.Models

[<CLIMutable>]
type Message =
    {
        Text : string
    }

[<CLIMutable>]
type AuthzCode = {
    Code: string
    State: string
}

[<CLIMutable>]
type AuthzErr = {
    Error: string
    State: string
}

open FSharp.Azure.StorageTypeProvider
open System

type Azure = AzureTypeProvider<tableSchema="TableSchema.json">

Azure.Tables.ZLAuthnAttempts |> ignore
Azure.Tables.ZLTokens |> ignore    

[<CLIMutable>]
type SuccessAuthnAttempt = {
    AuthzCode: string
    CodeExpired: bool
    ReplyReceivedAt: DateTime
    SuccessAuthorized: bool    
}

[<CLIMutable>]
type FailedAuthnAttempt = {
    ReplyReceivedAt: DateTime
    SuccessAuthorized: bool
    Error: string
}

[<CLIMutable>]
type Tokens = {
    AccessToken: string
    RefreshToken: string
}

[<CLIMutable>]
type AzStorage = {
    ConnStr: string
}