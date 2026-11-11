using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

/// <summary>
/// The cloud save's server (Cloud Code module "GameSave", SaveCloud.ModuleName). Players can't write Cloud Save themselves
/// (Assets/CloudCode/OnlineCity.ac), so their save and its meta only change here, after SaveRules has checked them. Every
/// endpoint returns a SaveResult as JSON text (the game reads it with Newtonsoft, the same as here):
/// - SubmitSave(request): request = SubmitRequest JSON.
/// - NewRun(runId, knownAcceptedAt): the player started a new company (idempotent for the current live run; superseded when
///   the server's live run got progress after knownAcceptedAt, the meta.AcceptedAt this client last saw; 0 when omitted).
/// - EndRun(runId, baseSeq, inFlightSeq, inFlightHash): Game Over for that run, only from a copy that is up to date with the
///   server (else stale, nothing changes; another run: no_run). Left-out parameters mean "unknown": the strictest answer.
/// </summary>
public class GameSaveModule
{
    readonly IGameApiClient api;
    readonly ILogger<GameSaveModule> logger;

    public GameSaveModule(IGameApiClient api, ILogger<GameSaveModule> logger)
    {
        this.api = api;
        this.logger = logger;
    }

    GameSaveService Service(IExecutionContext ctx)
    {
        return new GameSaveService(
            new UgsSaveStore(ctx, api),
            SaveCloud.UnixNow,
            message => logger.LogWarning("{Message} (player {Player})", message, ctx.PlayerId));
    }

    async Task<string> Handle(IExecutionContext ctx, string operation, Func<GameSaveService, Task<SaveResult>> call)
    {
        try
        {
            SaveResult result = await call(Service(ctx));
            return JsonConvert.SerializeObject(result);
        }
        catch (Exception e)
        {
            // Details stay in the log; the client only learns to try again later
            logger.LogError(e, "GameSave {Operation} failed for player {Player}", operation, ctx.PlayerId);
            return JsonConvert.SerializeObject(SaveResult.From(SaveCodes.Error, "The save server couldn't do that right now. Please try again.", null, SaveCloud.UnixNow()));
        }
    }

    [CloudCodeFunction(SaveCloud.FnSubmit)]
    public Task<string> SubmitSave(IExecutionContext ctx, string request)
        => Handle(ctx, SaveCloud.FnSubmit, s => s.Submit(request));

    // knownAcceptedAt defaults to 0 ("never synced", the strictest answer) for a caller that leaves it out
    [CloudCodeFunction(SaveCloud.FnNewRun)]
    public Task<string> NewRun(IExecutionContext ctx, string runId, long knownAcceptedAt = 0)
        => Handle(ctx, SaveCloud.FnNewRun, s => s.NewRun(runId, knownAcceptedAt));

    // Left-out chain parameters mean "unknown" (baseSeq -1, no upload in flight): only an already ended run answers accepted
    [CloudCodeFunction(SaveCloud.FnEndRun)]
    public Task<string> EndRun(IExecutionContext ctx, string runId, long baseSeq = -1, long inFlightSeq = -1, string inFlightHash = null)
        => Handle(ctx, SaveCloud.FnEndRun, s => s.EndRun(runId, baseSeq, inFlightSeq, inFlightHash));
}
