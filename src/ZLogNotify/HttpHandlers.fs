namespace ZLogNotify

open FSharp.Azure.StorageTypeProvider.Table
open Models
open System
open ZLogNotify.OAuth2Client.Zoho
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.V2.ContextInsensitive
open Giraffe
open Giraffe.HttpStatusCodeHandlers.Successful
open Giraffe.HttpStatusCodeHandlers.RequestErrors
open Giraffe.GoodRead.RequireImpl
open Giraffe.GoodRead
open System.Collections.Specialized
open Microsoft.Extensions.Options
open Microsoft.Extensions.DependencyInjection

module Services =
    type ZLAuthnAttempt = Azure.Domain.ZLAuthnAttemptsEntity
    type ZLTokens = Azure.Domain.ZLTokensEntity
    
    type IAuthnAttemptStore =
        abstract member InsertAttempt: stateStr:string -> Async<unit>
        abstract member Get: stateStr:string -> Async<ZLAuthnAttempt option>
        abstract member UpdateWithCode: stateStr:string * code:SuccessAuthnAttempt -> Async<unit>
        abstract member UpdateWithErr: stateStr:string * err:FailedAuthnAttempt -> Async<unit>

    type ITokensStore =
        abstract member Insert: tnow:DateTimeOffset * tokens: Tokens -> Async<unit>
        abstract member GetLatestTokens : unit -> Async<ZLTokens option>
        abstract member GetLatestValidTokens: unit -> Async<ZLTokens option>

    let AuthnAttemptStoreProvider =
        new Func<IServiceProvider, IAuthnAttemptStore>(fun (sp: IServiceProvider) ->
            let azConfig = sp.GetService<IOptionsMonitor<AzStorage>>()
            let connStr = azConfig.CurrentValue.ConnStr
            { new IAuthnAttemptStore with
                member this.InsertAttempt(stateStr) = Azure.Tables.ZLAuthnAttempts.InsertAsync(Partition "Attempt", Row stateStr, null, connectionString = connStr) |> Async.Ignore
                member this.Get(stateStr) = Azure.Tables.ZLAuthnAttempts.GetAsync(Row stateStr, Partition "Attempt", connStr)
                member this.UpdateWithCode(stateStr, code) = 
                    Azure.Tables.ZLAuthnAttempts
                        .InsertAsync(Partition "Attempt", Row stateStr, code, TableInsertMode.Upsert, connStr) |> Async.Ignore
                member this.UpdateWithErr(stateStr, err) =
                    Azure.Tables.ZLAuthnAttempts
                        .InsertAsync(Partition "Attempt", Row stateStr, err, TableInsertMode.Upsert, connStr) |> Async.Ignore })

    let pole = DateTime(2030, 1, 1).Ticks

    let TokensStoreProvider =
        new Func<IServiceProvider, ITokensStore>(fun (sp: IServiceProvider) ->
            let azConfig = sp.GetService<IOptionsMonitor<AzStorage>>()
            let connStr = azConfig.CurrentValue.ConnStr
            { new ITokensStore with
                member this.Insert(tnow, tokens) = 
                    async {
                        let id = 
                            (pole - tnow.UtcTicks - ZohoAccessTokenValidSpan.Ticks)
                                .ToString().PadLeft(16, '0')
                        do! (Azure.Tables.ZLTokens
                                .InsertAsync(Partition "ZohoTokens", Row id, tokens, connectionString = connStr)
                                |> Async.Ignore)
                    }
                member this.GetLatestTokens() =
                    async {
                        let! result =
                            Azure.Tables.ZLTokens.Query()
                                .``Where Partition Key Is``.``Equal To``("ZohoTokens")
                                .ExecuteAsync(1, connStr)
                        return Seq.tryHead result
                    }
                member this.GetLatestValidTokens() =
                    async {
                        let validPoint =
                            (pole - DateTimeOffset.Now.UtcTicks).ToString().PadLeft(16, '0')
                        let! tokenResults =
                            Azure.Tables.ZLTokens.Query()
                                .``Where Partition Key Is``.``Equal To``("ZohoTokens")
                                .``Where Row Key Is``.``Less Than``(validPoint)
                                .ExecuteAsync(1, connStr)
                        return Seq.tryHead tokenResults
                    }
                })
    
    let asFst snd fst = fst, snd
    let asSnd fst snd = fst, snd

module HttpHandlers =
    open Services

    let handleGetHello =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let response = {
                    Text = "Hello world, from Giraffe!"
                }
                return! json response next ctx
            }

    let zohoNotify () =
        let tnow = DateTime.Now
        tnow.Hour > 17


        ()


    let prepareLoginRedirect (store: IAuthnAttemptStore) (zhClient: ZohoClient) =
        task {
            let state = DateTime.Now.Ticks.ToString("x")
            let! baseStr = zhClient.GetLoginLinkUriAsync(state)
            let uri = 
                sprintf "%s&access_type=offline&prompt=none" baseStr
            do! store.InsertAttempt state
            return redirectTo false uri
        }

    let requestAuthorization =
        require {
            let! zhClient = service<ZohoClient>()
            let! authnAttpStore = service<IAuthnAttemptStore>()
            let! tokensStore = service<ITokensStore>()
            return task {
                let yieldOk =  task { return OK "already authorized" }
                let! tokenResults = tokensStore.GetLatestTokens()
                let! next =
                    match tokenResults with
                    | None ->
                        prepareLoginRedirect authnAttpStore zhClient
                    | Some tokens ->
                        let expired =
                            (tokens.RowKey |> Convert.ToInt64) > (pole - DateTimeOffset.Now.UtcTicks)
                        
                        if expired then
                            task {
                                try
                                    let! accessToken = zhClient.GetCurrentTokenAsync (tokens.RefreshToken, true)
                                    do! (
                                        { 
                                            AccessToken = zhClient.AccessToken; 
                                            RefreshToken = 
                                                String.IsNullOrEmpty zhClient.RefreshToken
                                                |> function
                                                | true -> tokens.RefreshToken
                                                | false -> zhClient.RefreshToken
                                        } 
                                        |> asSnd DateTimeOffset.Now
                                        |> tokensStore.Insert)
                                    return! yieldOk
                                with
                                    | _ ->
                                        return! prepareLoginRedirect authnAttpStore zhClient                            
                            }
                        else 
                            yieldOk
                return next
            }
        } |> Require.apply

    let notify =
        require {
            let! store = service<ITokensStore>()

            return task {
                match! store.GetLatestValidTokens() with
                | None ->
                    return (redirectTo false "/api/zoho/login")
                | Some tokens ->
                    //Todo: search zoho timesheet, send notifications to team members
                    return (OK "notifications triggered")
            }
        } |> Require.apply

    let exchangeForToken (zohoClient: ZohoClient) (payload: AuthzCode) =
        let nvc = NameValueCollection()
        nvc.Add("code", payload.Code)
        nvc.Add("state", payload.State)
        task {
            let! tokenStr = zohoClient.GetTokenAsync nvc
            return { AccessToken = zohoClient.AccessToken; RefreshToken = zohoClient.RefreshToken }
        }


    let receiveAuthzCode (payload: AuthzCode) =
        require {
            let! zohoClient = service<ZohoClient>()
            let! store = service<IAuthnAttemptStore>()
            let! tokenStore = service<ITokensStore>()
            return task {
                let failDefault = BAD_REQUEST "invalid request" 

                let! attemptOpt = store.Get(payload.State)
                let! handler =
                    match attemptOpt with
                    | None -> 
                        task { return failDefault }
                    | Some attempt ->
                        let tReplied = DateTimeOffset.Now
                        let expired = (tReplied - attempt.Timestamp) > ZohoAuthzCodeValidSpan
                        
                        let success = {
                            AuthzCode = payload.Code
                            CodeExpired = expired
                            ReplyReceivedAt = tReplied.UtcDateTime
                            SuccessAuthorized = not expired
                        }
                        task {
                            do! store.UpdateWithCode(payload.State, success)
                            
                            if not expired then
                                let! tokens = exchangeForToken zohoClient payload
                                do! tokenStore.Insert (tReplied, tokens)
                                return OK "ZLogNotify gets authorized"
                            else
                                return failDefault
                        }
                        
                return handler
            }
        } |> Require.apply
    

    let receiveAuthzErr (payload: AuthzErr) =
        require {
            let! store = service<IAuthnAttemptStore>()

            return task {
                let! attemptOpt = store.Get(payload.State)
                let! handler =
                    match attemptOpt with
                    | None -> task { return BAD_REQUEST payload.Error }
                    | Some attempt ->
                        let fails = {
                            ReplyReceivedAt = DateTimeOffset.Now.UtcDateTime
                            SuccessAuthorized = false
                            Error = payload.Error
                        }
                        task {
                            do! store.UpdateWithErr(payload.State, fails)
                            return BAD_REQUEST payload.Error
                        }
                return handler
            }
        } |> Require.apply


