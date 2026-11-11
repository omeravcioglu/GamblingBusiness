using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

/// <summary>
/// The online cities' server (Cloud Code module "OnlineCity"). Every endpoint takes the city ("ist", "lon", ...) and
/// runs one CityLogic operation of that city for the calling player, returning a CityResult as JSON text (the game
/// reads it with Newtonsoft, the same as here). Players can't write Cloud Save themselves (see
/// Assets/CloudCode/OnlineCity.ac), so this is the only way money, plots and logos change.
/// </summary>
public class CityModule
{
    readonly IGameApiClient api;
    readonly IPushClient push;
    readonly ILogger<CityModule> logger;

    public CityModule(IGameApiClient api, IPushClient push, ILogger<CityModule> logger)
    {
        this.api = api;
        this.push = push;
        this.logger = logger;
    }

    CityLogic Logic(IExecutionContext ctx, string city)
    {
        return new CityLogic(
            new UgsCityStore(ctx, api, city),
            city,
            new UgsCityNotifier(ctx, push, logger, city),
            null,
            (min, max) => Random.Shared.Next(min, max),
            message => logger.LogWarning("{Message}", message));
    }

    async Task<string> Handle(IExecutionContext ctx, string city, string operation, Func<CityLogic, Task<CityResult>> call)
    {
        if (!CityWorlds.Exists(city))
            return JsonConvert.SerializeObject(CityResult.Fail("That city doesn't exist."));
        try
        {
            var result = await call(Logic(ctx, city));
            return JsonConvert.SerializeObject(result);
        }
        catch (Exception e)
        {
            logger.LogError(e, "City {City} {Operation} failed for player {Player}", city, operation, ctx.PlayerId);
            return JsonConvert.SerializeObject(CityResult.Fail("The city couldn't do that right now. Please try again."));
        }
    }

    [CloudCodeFunction("GetAccount")]
    public Task<string> GetAccount(IExecutionContext ctx, string city)
        => Handle(ctx, city, "GetAccount", c => c.GetAccount(ctx.PlayerId));

    [CloudCodeFunction("Join")]
    public Task<string> Join(IExecutionContext ctx, string city, string name, string logo, string pixels)
        => Handle(ctx, city, "Join", c => c.Join(ctx.PlayerId, name, logo, pixels));

    [CloudCodeFunction("UpdateProfile")]
    public Task<string> UpdateProfile(IExecutionContext ctx, string city, string name, string logo, string pixels)
        => Handle(ctx, city, "UpdateProfile", c => c.UpdateProfile(ctx.PlayerId, name, logo, pixels));

    [CloudCodeFunction("Deposit")]
    public Task<string> Deposit(IExecutionContext ctx, string city, string opId, long amount)
        => Handle(ctx, city, "Deposit", c => c.Deposit(ctx.PlayerId, opId, amount));

    [CloudCodeFunction("Withdraw")]
    public Task<string> Withdraw(IExecutionContext ctx, string city, string opId, long amount)
        => Handle(ctx, city, "Withdraw", c => c.Withdraw(ctx.PlayerId, opId, amount));

    [CloudCodeFunction("CollectRent")]
    public Task<string> CollectRent(IExecutionContext ctx, string city)
        => Handle(ctx, city, "CollectRent", c => c.CollectRent(ctx.PlayerId));

    [CloudCodeFunction("BuyPlot")]
    public Task<string> BuyPlot(IExecutionContext ctx, string city, string opId, string plotId, long price)
        => Handle(ctx, city, "BuyPlot", c => c.BuyPlot(ctx.PlayerId, opId, plotId, price));

    [CloudCodeFunction("StartChallenge")]
    public Task<string> StartChallenge(IExecutionContext ctx, string city, string opId, string plotId)
        => Handle(ctx, city, "StartChallenge", c => c.StartChallenge(ctx.PlayerId, opId, plotId));

    [CloudCodeFunction("ChallengeMove")]
    public Task<string> ChallengeMove(IExecutionContext ctx, string city, string opId, string move)
        => Handle(ctx, city, "ChallengeMove", c => c.ChallengeMove(ctx.PlayerId, opId, move));

    [CloudCodeFunction("ReportLogo")]
    public Task<string> ReportLogo(IExecutionContext ctx, string city, string plotId)
        => Handle(ctx, city, "ReportLogo", c => c.ReportLogo(ctx.PlayerId, plotId));

    [CloudCodeFunction("MarkInboxRead")]
    public Task<string> MarkInboxRead(IExecutionContext ctx, string city)
        => Handle(ctx, city, "MarkInboxRead", c => c.MarkInboxRead(ctx.PlayerId));
}
