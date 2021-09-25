#ifndef RPCSANITYNATIVE_RATELIMITER_H
#define RPCSANITYNATIVE_RATELIMITER_H

#include <unordered_set>
#include <unordered_map>
#include <chrono>
#include <string>
#include <mutex>
#include <thread>
#include <queue>

namespace RPCSanityNative {

class RateLimiter {
public:
    RateLimiter();
    ~RateLimiter();

    void cleanupAfterDeparture();
    void onlyAllowedPerSecond(const std::string& eventName, std::int32_t amount);
    void forgiveUser(std::int32_t senderID);
    bool isRateLimited(std::int32_t senderID);
    void blacklistUser(std::int32_t senderID);
    bool isSafeToRun(const std::string& eventName, std::int32_t senderID);
private:
    void addUserToBlacklist(std::int32_t senderID);
    void forgiveUserThread();

    std::atomic_bool m_run;
    std::unordered_map<std::int32_t, std::unordered_map<std::string, std::int32_t>> m_sendsPerSecond;
    std::unordered_map<std::string, std::int32_t> m_allowedSendsPerSecond;
    std::mutex l_blacklistedUsers;
    std::unordered_set<std::int32_t> m_blacklistedUsers;
    std::chrono::high_resolution_clock::time_point m_clearRecordsTime;

    struct QueuedForgiveUser {
        std::chrono::high_resolution_clock::time_point forgiveTime;
        std::int32_t senderID;
    };

    std::mutex l_usersToForgiveQueue;
    std::queue<QueuedForgiveUser> m_usersToForgiveQueue;

    std::thread m_forgiveUserThread;
};

}

#endif // RPCSANITYNATIVE_RATELIMITER_H
