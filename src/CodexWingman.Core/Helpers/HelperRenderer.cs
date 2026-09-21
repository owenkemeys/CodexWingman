using System.Text.Json;

namespace CodexWingman.Core.Helpers;

public sealed class HelperRenderer
{
    private const string QueueName = "__codexWingmanHostActions";
    private const string AgentDerangementRiskId = "agent-derangement-risk";
    private const string AgentDerangementRiskLeaseRefresh = "window.__codexHelperAgentDerangementRiskLeaseExpiresAt=Date.now()+6000;\n";

    public string BuildApply(
        HelperPackage package,
        JsonElement state,
        string targetId,
        JsonElement? bootstrap = null,
        string? childOperationToken = null)
    {
        var helperId = JsonSerializer.Serialize(package.Manifest.Id);
        var helperConfig = SerializeHelperConfig(package.Manifest.Config);
        var target = JsonSerializer.Serialize(new { id = targetId });
        var operationToken = JsonSerializer.Serialize(childOperationToken);
        var source = package.ApplySource;
        var livenessLeaseRefresh = package.Manifest.Id.Equals(AgentDerangementRiskId, StringComparison.Ordinal)
            ? AgentDerangementRiskLeaseRefresh
            : string.Empty;
        return "(() => {\n"
            + "const helperId=" + helperId + ";const state=" + state.GetRawText()
            + ";const bootstrap=" + (bootstrap?.GetRawText() ?? "null") + ";\n"
            + "const helperConfig=Object.freeze(" + helperConfig + ");\n"
            + "const childOperationToken=" + operationToken + ";let childCompletionSent=false;\n"
            + "const queue=Array.isArray(window." + QueueName + ")?window." + QueueName + ":(window." + QueueName + "=[]);\n"
            + "const wingman=Object.freeze({helperId,target:Object.freeze(" + target + "),completeChild(){if(childCompletionSent||childOperationToken===null)return false;childCompletionSent=true;queue.push({helperId,action:'wingman.completeChild',payload:{token:childOperationToken}});return true;},openChild(path,bootstrap=null){queue.push({helperId,action:'wingman.openChild',payload:{path,bootstrap}});},request(action,payload=null){queue.push({helperId:"
            + helperId + ",action,payload});}});\n"
            + livenessLeaseRefresh + source + "\n"
            + "})()";
    }

    public string BuildRemove(HelperPackage package)
    {
        var helperId = JsonSerializer.Serialize(package.Manifest.Id);
        var helperConfig = SerializeHelperConfig(package.Manifest.Config);
        var source = package.RemoveSource;
        var legacyUsageCleanup = package.Manifest.Id.Equals("usage-dials", StringComparison.Ordinal)
            ? "try{window.__codexHelperUsageDials?.cleanup?.();}catch{}"
            : string.Empty;
        return "(() => {\n"
            + "const helperId=" + helperId + ";const state=null;\n"
            + "const helperConfig=Object.freeze(" + helperConfig + ");\n"
            + "const queue=Array.isArray(window." + QueueName + ")?window." + QueueName + ":(window." + QueueName + "=[]);\n"
            + "const wingman=Object.freeze({helperId,request(action,payload=null){queue.push({helperId:"
            + helperId + ",action,payload});}});\n"
            + "let removalError=null;try{\n" + source + "\n}catch(error){removalError=error;}finally{\n"
            + legacyUsageCleanup + "\n"
            + "queue.splice(0,queue.length,...queue.filter(request=>request?.helperId!==helperId));}\n"
            + "if(removalError)throw removalError;\n"
            + "})()";
    }

    public string BuildDrainActions() =>
        $"(() => {{ const queue=window.{QueueName}; if(!Array.isArray(queue)) return []; return queue.splice(0,queue.length); }})()";

    private static string SerializeHelperConfig(JsonElement? config) =>
        config is { ValueKind: JsonValueKind.Object } objectConfig
            ? objectConfig.GetRawText()
            : "{}";
}
