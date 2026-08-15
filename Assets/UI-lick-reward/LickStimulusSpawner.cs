/// <summary>
/// Provides the LickStimulusSpawner class that spawns UI indicators for lick and stimulus MQTT events.
/// </summary>
using System.Threading;
using Gimbl;
using SL.Tasks;
using UnityEngine;

namespace SL.UI
{
    /// <summary>
    /// Spawns UI indicator prefabs on a canvas in response to lick and stimulus MQTT messages.
    /// </summary>
    public class LickStimulusSpawner : MonoBehaviour
    {
        /// <summary>The prefab to instantiate when a lick is detected.</summary>
        public GameObject lickPrefab;

        /// <summary>The prefab to instantiate when a stimulus is delivered.</summary>
        public GameObject stimulusPrefab;

        /// <summary>The canvas where UI indicator prefabs will be spawned.</summary>
        public Canvas canvas;

        /// <summary>The MQTT channel for receiving lick detection messages.</summary>
        private MQTTChannel _lick;

        /// <summary>The MQTT channel for receiving stimulus outcome messages.</summary>
        private MQTTChannel<StimulusTriggerZone.StimulusMessage> _stimulus;

        /// <summary>The number of lick indicators awaiting instantiation on the next Update.</summary>
        /// <remarks>
        /// The broker delivery path invokes the channel callback on an MQTTnet worker thread while Update reads the
        /// count on the main thread, so every access goes through <see cref="Interlocked"/> or
        /// <see cref="Volatile"/>. A count rather than a flag renders one indicator per event when several events
        /// land inside a single frame.
        /// </remarks>
        private int _pendingLickCount;

        /// <summary>The number of stimulus indicators awaiting instantiation on the next Update.</summary>
        private int _pendingStimulusCount;

        /// <summary>Determines whether the unassigned canvas or prefab error has already been reported.</summary>
        private bool _missingReferenceReported;

        /// <summary>Sets up MQTT channels and registers event listeners.</summary>
        private void Start()
        {
            _lick = new MQTTChannel(MQTTTopics.Interaction, isListener: true);
            _lick.receivedEvent.AddListener(OnLick);
            _stimulus = new MQTTChannel<StimulusTriggerZone.StimulusMessage>(MQTTTopics.Stimulus, isListener: true);
            _stimulus.receivedEvent.AddListener(OnStimulus);
        }

        /// <summary>Spawns one UI indicator per pending event on the main thread.</summary>
        private void Update()
        {
            SpawnPending(ref _pendingLickCount, lickPrefab, "lick");
            SpawnPending(ref _pendingStimulusCount, stimulusPrefab, "stimulus");
        }

        /// <summary>Removes the MQTT event listeners and the channel routing entries when destroyed.</summary>
        private void OnDestroy()
        {
            if (_lick != null)
            {
                _lick.receivedEvent.RemoveListener(OnLick);
                ReleaseChannel(_lick);
            }

            if (_stimulus != null)
            {
                _stimulus.receivedEvent.RemoveListener(OnStimulus);
                ReleaseChannel(_stimulus);
            }
        }

        /// <summary>Records one pending lick indicator to be spawned on the next Update cycle.</summary>
        private void OnLick()
        {
            Interlocked.Increment(ref _pendingLickCount);
        }

        /// <summary>Records one pending stimulus indicator on the next Update cycle when one is delivered.</summary>
        /// <param name="message">The stimulus outcome message reporting whether the stimulus was delivered.</param>
        private void OnStimulus(StimulusTriggerZone.StimulusMessage message)
        {
            if (message.delivered)
            {
                Interlocked.Increment(ref _pendingStimulusCount);
            }
        }

        /// <summary>Instantiates one copy of the supplied prefab per pending event on the canvas.</summary>
        /// <remarks>
        /// The count is consumed only once the canvas and the prefab both resolve, so an unassigned reference holds
        /// the indicators for the frame that follows its assignment. The consuming exchange runs before the
        /// instantiation loop, so an event arriving on the worker thread meanwhile is carried to the next frame.
        /// </remarks>
        /// <param name="pendingCount">The pending event count, cleared once the indicators are instantiated.</param>
        /// <param name="prefab">The UI prefab instantiated once per pending event.</param>
        /// <param name="indicatorName">The indicator kind named in the unassigned-reference error.</param>
        private void SpawnPending(ref int pendingCount, GameObject prefab, string indicatorName)
        {
            if (Volatile.Read(ref pendingCount) == 0)
            {
                return;
            }

            if (canvas == null || prefab == null)
            {
                ReportMissingReference(indicatorName);
                return;
            }

            Transform parent = canvas.transform;
            int pending = Interlocked.Exchange(ref pendingCount, 0);
            for (int index = 0; index < pending; index++)
            {
                Instantiate(prefab, parent);
            }
        }

        /// <summary>Reports an unassigned canvas or prefab once, naming the indicator that cannot be spawned.
        /// </summary>
        /// <remarks>
        /// Update retries every frame for as long as an indicator stays pending, so the report is emitted once to
        /// keep a single misconfiguration from filling the console.
        /// </remarks>
        /// <param name="indicatorName">The indicator kind that cannot be spawned.</param>
        private void ReportMissingReference(string indicatorName)
        {
            if (_missingReferenceReported)
            {
                return;
            }
            _missingReferenceReported = true;

            string unassignedField = canvas == null ? "canvas" : "prefab";
            string message =
                $"Unable to spawn the {indicatorName} indicator of the LickStimulusSpawner on '{name}'. The "
                + $"{unassignedField} field must reference an assigned object, but it is unassigned. The pending "
                + "indicators are held until the reference resolves.";
            Debug.LogError(message);
        }

        /// <summary>Removes a channel from the routing list of the client that delivers its messages.</summary>
        /// <param name="channel">The channel to detach from its client.</param>
        private static void ReleaseChannel(MQTTChannel channel)
        {
            if (channel.client != null)
            {
                channel.client.Unsubscribe(channel);
            }
        }
    }
}
