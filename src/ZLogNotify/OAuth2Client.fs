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
            member this.set_ClientTypeName(value) = ()
            member this.IsEnabled = zcc.IsEnabled
            member this.set_IsEnabled(value) = ()
            member this.ClientId = zcc.ClientId
            member this.set_ClientId(value) = ()
            member this.ClientSecret = zcc.ClientSecret
            member this.set_ClientSecret(value) = ()
            member this.ClientPublic = zcc.ClientPublic
            member this.set_ClientPublic(value) = ()
            member this.Scope = zcc.Scope
            member this.set_Scope(value) = ()
            member this.RedirectUri = zcc.RedirectUri
            member this.set_RedirectUri(value) = ()}

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