#include "ratelimiter.h"

RPCSanityNative::RateLimiter::RateLimiter()
    : m_run()
    , m_sendsPerSecond()
    , m_allowedSendsPerSecond()
    , m_blacklistedUsers()
    , m_clearRecordsTime(std::chrono::high_resolution_clock::now() + std::chrono::seconds(1))
    , m_forgiveUserThread()
{
    m_run.store(true, std::memory_order::relaxed);
    m_forgiveUserThread = std::thread(&RPCSanityNative::RateLimiter::forgiveUserThread, this);
}

RPCSanityNative::RateLimiter::~RateLimiter()
{
    m_run.store(false, std::memory_order::relaxed);
    if (m_forgiveUserThread.joinable()) {
        m_forgiveUserThread.join();
    }
}

void RPCSanityNative::RateLimiter::cleanupAfterDeparture()
{
    {
        std::scoped_lock l(l_blacklistedUsers);
        m_blacklistedUsers.clear();
    }

    m_sendsPerSecond.clear();
}

void RPCSanityNative::RateLimiter::onlyAllowedPerSecond(const std::string& eventName, std::int32_t amount)
{
    // Will not change the value if it already exists
    m_allowedSendsPerSecond.insert(std::pair<std::string, std::int32_t>(eventName, amount));
}

void RPCSanityNative::RateLimiter::forgiveUser(std::int32_t senderID)
{
    std::scoped_lock l(l_blacklistedUsers);
    auto it = m_blacklistedUsers.find(senderID);

    if (it != m_blacklistedUsers.end()) {
        m_blacklistedUsers.erase(it);
    }
}

bool RPCSanityNative::RateLimiter::isRateLimited(std::int32_t senderID)
{
    std::scoped_lock l(l_blacklistedUsers);
    return m_blacklistedUsers.contains(senderID);
}

void RPCSanityNative::RateLimiter::blacklistUser(std::int32_t senderID)
{
    if (!isRateLimited(senderID)) {
        addUserToBlacklist(senderID);
    }
}

bool RPCSanityNative::RateLimiter::isSafeToRun(const std::string& eventName, std::int32_t senderID)
{
    if (isRateLimited(senderID)) {
        return false;
    }

    if (m_clearRecordsTime <= std::chrono::high_resolution_clock::now()) {
        m_clearRecordsTime = std::chrono::high_resolution_clock::now() + std::chrono::seconds(1);

        for (auto& sends : m_sendsPerSecond) {
            sends.second.clear();
        }
    } else {
        auto& sendsForSender = m_sendsPerSecond[senderID];

        // Inserts 1 if the key doesnt exist, and increments if it does exist
        std::int32_t rateLimit = ++sendsForSender.insert(std::pair<std::string, std::int32_t>(eventName, 0)).first->second;

        auto allowedSendsPerSecond = m_allowedSendsPerSecond.find(eventName);
        if (allowedSendsPerSecond == m_allowedSendsPerSecond.end()) {
            return true;
        }

        if (rateLimit > allowedSendsPerSecond->second) {
            addUserToBlacklist(senderID);
            return false;
        }
    }

    return true;
}

void RPCSanityNative::RateLimiter::addUserToBlacklist(std::int32_t senderID)
{
    {
        std::scoped_lock l(l_blacklistedUsers);
        m_blacklistedUsers.insert(senderID);
    }

    {
        std::scoped_lock l(l_usersToForgiveQueue);
        m_usersToForgiveQueue.push(QueuedForgiveUser{ std::chrono::high_resolution_clock::now() + std::chrono::seconds(30), senderID });
    }
}

void RPCSanityNative::RateLimiter::forgiveUserThread()
{
    bool doForgive = false;
    std::int32_t senderID = 0;
    std::chrono::high_resolution_clock::time_point nextWake;

    while (m_run.load(std::memory_order::relaxed)) {
        // Configure sleep for 20 milliseconds
        nextWake = std::chrono::high_resolution_clock::now() + std::chrono::milliseconds(20);

        // Check if we have a user to forgive, but DO NOT forgive hime while having "l_usersToForgiveQueue" locked, this could result in a deadlock
        {
            std::scoped_lock l(l_usersToForgiveQueue);
            std::size_t nQueued = m_usersToForgiveQueue.size();

            if (nQueued > 0) {
                auto nextVal = m_usersToForgiveQueue.front();

                if (nextVal.forgiveTime >= std::chrono::high_resolution_clock::now()) {
                    senderID = nextVal.senderID;
                    doForgive = true;
                    m_usersToForgiveQueue.pop();
                }

                if (nQueued > 1) {
                    // Set sleep until next forgive time
                    nextWake = m_usersToForgiveQueue.front().forgiveTime;
                }
            }
        }

        // Forgive the user
        if (doForgive) {
            forgiveUser(senderID);
            doForgive = false;
        }

        // Sleep for configured amount of ms
        std::this_thread::sleep_until(nextWake);
    }
}
