namespace ZLogNotify.OAuth2Client

open OAuth2.Client
open OAuth2.Infrastructure
open OAuth2.Configuration
open System
open Microsoft.Extensions.Options

module Zoho =
    [<CLIMutable>]
    type ZohoClientConfig = {
        ClientTypeName: string
        IsEnabled: bool
        ClientId: string
        ClientSecret: string
        ClientPublic: string
        Scope: string
        RedirectUri: string
    }
    
    let toClientCfg (configOption: IOptionsSnapshot<ZohoClientConfig>) =
        let zcc = configOption.Value
        {new IClientConfiguration with
            member this.ClientTypeName = zcc.ClientTypeName
            member this.IsEnabled = zcc.IsEnabled
            member this.ClientId = zcc.ClientId
            member this.ClientSecret = zcc.ClientSecret
            member this.ClientPublic = zcc.ClientPublic
            member this.Scope = zcc.Scope
            member this.RedirectUri = zcc.RedirectUri}

    type ZohoClient (factory: IRequestFactory, configOption: IOptionsSnapshot<ZohoClientConfig>) = 
        inherit OAuth2Client(factory, configOption |> toClientCfg)
        override zc.get_Name() = "Zoho"
        override zc.get_AccessCodeServiceEndpoint() =
            Endpoint (BaseUri = "https://accounts.zoho.com", Resource = "/oauth/v2/auth")
        override zc.get_AccessTokenServiceEndpoint() =
            Endpoint (BaseUri = "https://accounts.zoho.com", Resource = "/oauth/v2/token")
        override zc.get_UserInfoServiceEndpoint() =
            raise (NotImplementedException())
        override zc.ParseUserInfo(content) =
            raise (NotImplementedException())

    let ZohoAuthzCodeValidSpan = TimeSpan(0, 2, 0)
    let ZohoAccessTokenValidSpan = TimeSpan(1, 0, 0)