using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.SceneManagement;

namespace NetworkSanity.Core
{
    internal class RateLimiter
    {
        // Holds the current amount of sent events per player for the current second
        // The key is their ActorID, the value is a dictionary holding records
        // The dictionary will contain string keys which are the names of RPCs and events
        // The values will be how many times that event has been sent in the current second
        private readonly IDictionary<int, IDictionary<string, int>> SendsPerSecond;

        // Holds the amount of allocated sends per second for each type of event
        // The key is a string which holds the name of an event or RPC
        // The value is an integer which holds the amount allowed per second
        // Once this value has been passed, that particular event will be ignored for one second
        // The offender will also be added to the BlacklistedUsers collection
        // By default, this makes all their events automatically blocked
        // You could create a new thread which forgives them after 3 seconds or however long needed
        private readonly IDictionary<string, int> AllowedSendsPerSecond;

        // Users which have broken the anti-spam limiter are stored here
        // It's a hashset because there can't be duplicate photon IDs
        // Default functionality will permanently ignore people once they break the limiter
        private readonly HashSet<int> BlacklistedUsers;

        // This is compared on each send to make sure 1 second hasn't passed
        private DateTime CurrentTime = DateTime.Now;

        // This function should be called whenever you leave or join a room
        // It will clean up everything to make sure there's no lingering bad records
        public void CleanupAfterDeparture()
        {
            lock (BlacklistedUsers)
                BlacklistedUsers.Clear();

            SendsPerSecond.Clear();

            // We don't clear AllowedSendsPerSecond
            // That collection just holds the max limits
            // It shouldn't change
        }

        // This should be placed inside of the RPC/Event which you want to rate limit
        // This will only be ran a single time and it will initialize the amount of sends
        public void OnlyAllowPerSecond(string eventName, int amount)
        {
            if (AllowedSendsPerSecond == null)
                return;

            // We only need to set the anti-spam value once
            if (AllowedSendsPerSecond.ContainsKey(eventName))
                return;

            AllowedSendsPerSecond[eventName] = amount;
        }

        // Removes the peer from the list of blacklisted users
        // Blacklisted users have all events marked as unsafe automatically
        public void ForgiveUser(int peerID)
        {
            lock (BlacklistedUsers)
                BlacklistedUsers.Remove(peerID);
        }

        public bool IsRateLimited(int senderID)
        {
            return BlacklistedUsers.Contains(senderID);
        }

        public void BlacklistUser(int senderID)
        {
            if (!IsRateLimited(senderID))
            {
                lock (BlacklistedUsers)
                    BlacklistedUsers.Add(senderID);

                new Thread(() =>
                {
                    Thread.Sleep(30000);
                    ForgiveUser(senderID);
                }).Start();
            }
        }

        // When someone sends us an event/rpc, this method should be called within the body of the function
        // If they send more than what should be possible, we should ignore their data for a while
        // This function will return true if the event/rpc is safe to run
        // If this function returns false, you should stop executing the event immediately
        public bool IsSafeToRun(string eventName, int senderID)
        {
            if (SendsPerSecond == null || AllowedSendsPerSecond == null)
                return true;

            if (BlacklistedUsers.Contains(senderID))
                return false;

            // If their ID is below or equal to 0
            // It means the event is from the server
            // We should always run this for safety
            //if (senderID <= 0)
            //    return true;

            // If one second has passed, we should reset all the records
            if (DateTime.Now.Subtract(CurrentTime).TotalSeconds > 1)
            {
                CurrentTime = DateTime.Now;

                // We don't need to lock this dictionary while clearing it
                // Photon runs on the Unity thread so there's no multi-dimensional errors
                foreach (var sends in SendsPerSecond)
                {
                    if (sends.Value == null)
                        continue;

                    sends.Value.Clear();
                }
            }

            else
            {
                // We only add new entries when an ActorID we haven't encountered appears
                // This is to save memory so we're not generating any obsolete entries
                if (!SendsPerSecond.ContainsKey(senderID))
                    SendsPerSecond.Add(senderID, new Dictionary<string, int>());

                // This is their first send, so we should initialize it as 1
                if (!SendsPerSecond[senderID].ContainsKey(eventName))
                    SendsPerSecond[senderID][eventName] = 1;
                else
                    SendsPerSecond[senderID][eventName]++;

                // A small check incase we forgot to setup the ratelimit
                if (!AllowedSendsPerSecond.ContainsKey(eventName))
                {
                    // Logger.Log($"{eventName} hasn't had the limiter set!");
                    return true;
                }

                // If they have passed the rate limit, we should ignore them
                if (SendsPerSecond[senderID][eventName] > AllowedSendsPerSecond[eventName])
                {
                    lock (BlacklistedUsers)
                        BlacklistedUsers.Add(senderID);

                    // Optional code to forgive after 3 seconds (thread-safe)

                    new Thread(() =>
                    {
                        Thread.Sleep(30000);
                        ForgiveUser(senderID);
                    }).Start();

                    return false;
                }
            }

            // Everything checks out
            return true;
        }

        public RateLimiter()
        {
            SendsPerSecond = new Dictionary<int, IDictionary<string, int>>();
            AllowedSendsPerSecond = new Dictionary<string, int>();
            BlacklistedUsers = new HashSet<int>();
            CurrentTime = DateTime.Now;

            SceneManager.add_sceneUnloaded(new Action<Scene>(s =>
            {
                CleanupAfterDeparture();
            }));
        }
    }
}
