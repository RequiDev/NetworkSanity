#include "ratelimiter.h"

#include <span>
#include <stdlib.h>
#include <string.h>

RPCSanityNative::RateLimiter rateLimiter;
std::unordered_map<std::string_view, std::pair<std::int32_t, std::int32_t>> rateLimitValues {
    { "Generic", {500, 500} },
    { "ReceiveVoiceStatsSyncRPC", {348, 64} },
    { "InformOfBadConnection", {64, 6} },
    { "initUSpeakSenderRPC", {256, 6} },
    { "InteractWithStationRPC", {128, 32} },
    { "SpawnEmojiRPC", {128, 6} },
    { "SanityCheck", {256, 32} },
    { "PlayEmoteRPC", {256, 6} },
    { "TeleportRPC", {256, 16} },
    { "CancelRPC", {256, 32} },
    { "SetTimerRPC", {256, 64} },
    { "_DestroyObject", {512, 128} },
    { "_InstantiateObject", {512, 128} },
    { "_SendOnSpawn", {512, 128} },
    { "ConfigurePortal", {512, 128} },
    { "UdonSyncRunProgramAsRPC", {512, 128} }, // <--- Udon is gay
    { "ChangeVisibility", {128, 12} },
    { "PhotoCapture", {128, 32} },
    { "TimerBloop", {128, 16} },
    { "ReloadAvatarNetworkedRPC", {128, 12} },
    { "InternalApplyOverrideRPC", {512, 128} },
    { "AddURL", {64, 6} },
    { "Play", {64, 6} },
    { "Pause", {64, 6} },
    { "SendVoiceSetupToPlayerRPC", {512, 6} },
    { "SendStrokeRPC", {512, 32} }
};
std::unordered_map<std::string_view, std::int32_t> rpcParameterCount {
    { "ReceiveVoiceStatsSyncRPC", 3 },
    { "InformOfBadConnection", 2 },
    { "initUSpeakSenderRPC", 1 },
    { "InteractWithStationRPC", 1 },
    { "SpawnEmojiRPC", 1 },
    { "SanityCheck", 3 },
    { "PlayEmoteRPC", 1 },
    { "CancelRPC", 0 },
    { "SetTimerRPC", 1 },
    { "_DestroyObject", 1 },
    { "_InstantiateObject", 4 },
    { "_SendOnSpawn", 1 },
    { "ConfigurePortal", 3 },
    { "UdonSyncRunProgramAsRPC", 1 },
    { "ChangeVisibility", 1 },
    { "PhotoCapture", 0 },
    { "TimerBloop", 0 },
    { "ReloadAvatarNetworkedRPC", 0 },
    { "InternalApplyOverrideRPC", 1 },
    { "AddURL", 1 },
    { "Play", 0 },
    { "Pause", 0 },
    { "SendVoiceSetupToPlayerRPC", 0 },
    { "SendStrokeRPC", 1 }
};

extern "C" __declspec(dllexport) void Native_Initialize()
{
    for (const auto& value : rateLimitValues) {
        std::string valueFirst(value.first);
        rateLimiter.onlyAllowedPerSecond("G_" + valueFirst, value.second.first);
        rateLimiter.onlyAllowedPerSecond(valueFirst, value.second.second);
    }
}

extern "C" __declspec(dllexport) void Native_CleanupAfterDeparture()
{
    rateLimiter.cleanupAfterDeparture();
}

extern "C" __declspec(dllexport) bool Native_IsRateLimited(std::int32_t senderID)
{
    return rateLimiter.isRateLimited(senderID);
}

extern "C" __declspec(dllexport) bool Native_IsSafeSender(std::int32_t senderID)
{
    return !rateLimiter.isRateLimited(senderID) &&
            rateLimiter.isSafeToRun("Generic", 0); // Failsafe to prevent extremely high amounts of RPCs passing through
}

extern "C" __declspec(dllexport) void Native_BlackListSender(std::int32_t senderID)
{
    rateLimiter.blacklistUser(senderID);
}

// Will return -1 for a exit with true, -2 for a exit with false, and 0 or more for a continue where the return length is the expected length of the parameters
extern "C" __declspec(dllexport) std::int32_t Native_IsSafeToRun(std::int32_t senderID, const char* parameterStringPtr, std::int32_t parameterStringLength, std::int32_t paramBytesLength)
{
    std::string_view parameterStringView(parameterStringPtr, parameterStringLength);
    std::string parameterString(parameterStringView);

    if (!rateLimiter.isSafeToRun("G_" + parameterString, 0) || !rateLimiter.isSafeToRun(parameterString, senderID)) {
        return -2;
    }

    auto paramCountIt = rpcParameterCount.find(parameterStringView);
    if (paramCountIt == rpcParameterCount.end()) {
        return -1; // we don't have any information about this RPC. Let it slide.
    }
    std::int32_t paramCount = paramCountIt->second;

    if (paramCount == 0 && paramBytesLength > 0) {
        rateLimiter.blacklistUser(senderID);
        return -2;
    }

    if (paramCount > 0 && paramBytesLength == 0) {
        rateLimiter.blacklistUser(senderID);
        return -2;
    }

    return paramCount;
}
