module ZLogNotify.App

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open Giraffe
open Giraffe.Core
open Giraffe.HttpStatusCodeHandlers.Successful
open ZLogNotify.HttpHandlers
open ZLogNotify.Models
open ZLogNotify.OAuth2Client.Zoho
open OAuth2.Infrastructure
open ZLogNotify.Services

// ---------------------------------
// Web app
// ---------------------------------

let nextBranch (next: HttpFunc) (ctx: HttpContext) = skipPipeline

let peekQuery<'T> = tryBindQuery<'T> (fun _ -> nextBranch) None

let webApp =
    choose [
        subRoute "/api"
            (choose [
                GET >=> choose [
                    route "/hello" >=> handleGetHello
                    route "/zoho/login" >=> requestAuthorization
                    route "/zoho/notify" >=> notify
                ]
            ])
        route "/grantConsume" >=> choose [
            peekQuery<AuthzCode> receiveAuthzCode
            peekQuery<AuthzErr> receiveAuthzErr
            OK "hello"
        ]
        setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureAppConfig (ctx: WebHostBuilderContext) (config: IConfigurationBuilder) =
    let environment = ctx.HostingEnvironment.EnvironmentName
    config
        .SetBasePath(System.IO.Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional = true)
        .AddJsonFile(sprintf "appsettings.%s.json" environment, optional = true)
        .AddEnvironmentVariables()
    |> ignore


let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IHostingEnvironment>()
    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseHttpsRedirection()
        .UseCors(configureCors)
        .UseGiraffe(webApp)

let configureServices (hostCtx: WebHostBuilderContext) (services : IServiceCollection) =
    let config = hostCtx.Configuration
    services.AddCors()    |> ignore
    services.AddGiraffe() |> ignore
    services.Configure<ZohoClientConfig>(config.GetSection("zoho:appRegistration")) |> ignore
    services.Configure<AzStorage>(config.GetSection("azure:storage")) |> ignore
    services.AddScoped<IRequestFactory, RequestFactory>() |> ignore
    services.AddScoped<ZohoClient>() |> ignore
    services.AddScoped<IAuthnAttemptStore>(AuthnAttemptStoreProvider) |> ignore
    services.AddScoped<ITokensStore>(TokensStoreProvider) |> ignore

let configureLogging (builder : ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Error)
           .AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .UseIISIntegration()
        .ConfigureAppConfiguration(configureAppConfig)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()
    0