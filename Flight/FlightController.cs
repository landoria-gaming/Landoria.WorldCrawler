using System;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Moves one local player while retaining ordinary networking, damage, and death handling.
    internal sealed partial class FlightController
    {
        private static FlightController current;
        private Player player;
        private ZNet network;
        private ZNetView view;
        private FlightPhysics physics;
        private AchievementGuard achievements;
        private long worldUid;
        private Vector3 expectedPosition;
        private float nextAudit;
        private float minimumHealth;
        private bool restoreOriginalMotion;
        private Vector3 transitFrom;
        private Vector3 transitExit;
        private float transitStarted;
        public Vector3 Origin
        {
            get; private set;
        }
        public Quaternion OriginRotation
        {
            get; private set;
        }
        public bool Active
        {
            get; private set;
        }
        public float Speed { get; set; } = 35f;
        public string AchievementSummary
        {
            get; private set;
        }
        public bool? AchievementEligible
        {
            get; private set;
        }
        public bool EmergencyReturnRequested
        {
            get; private set;
        }
        public bool NativeTransitActive
        {
            get; private set;
        }
        public bool NativeTransitSucceeded
        {
            get; private set;
        }
        public bool CanEndAtOrigin => Active && player != null &&
            Vector3.Distance(player.transform.position, Origin) <= 0.25f;

        // Starts only from a stable, living local player in a supported game version.
        public void Begin(Player value, Vector3? returnOrigin = null, Quaternion? returnRotation = null)
        {
            if (Active || current != null)
            {
                throw new InvalidOperationException("A controlled flight is already active.");
            }
            ValidateStart(value, returnOrigin.HasValue);
            player = value;
            network = ZNet.instance;
            worldUid = network.GetWorldUID();
            view = player.GetComponent<ZNetView>();
            achievements = new AchievementGuard(player);
            AchievementSummary = achievements.Summary;
            AchievementEligible = achievements.Eligible;
            physics = new FlightPhysics(player);
            Origin = returnOrigin ?? player.transform.position;
            OriginRotation = returnRotation ?? player.transform.rotation;
            if (!Finite(Origin) || !FiniteRotation(OriginRotation))
            {
                throw new ArgumentException("The recorded return origin is invalid.");
            }
            restoreOriginalMotion = !returnOrigin.HasValue;
            expectedPosition = player.transform.position;
            nextAudit = 0f;
            minimumHealth = Mathf.Max(10f, player.GetHealth() * 0.5f);
            EmergencyReturnRequested = false;
            Acquire();
        }

        // Advances a bounded continuous step or holds at the target while a zone loads.
        public bool Tick(Vector3 target, float deltaTime)
        {
            ValidateActive();
            if (!Finite(target) || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                float.IsNaN(Speed) || float.IsInfinity(Speed))
            {
                throw new ArgumentException("Flight coordinates and elapsed time must be finite.");
            }
            if (Time.unscaledTime >= nextAudit)
            {
                achievements.Validate();
                nextAudit = Time.unscaledTime + 1f;
            }
            float step = Mathf.Clamp(Speed, 1f, 100f) * Mathf.Clamp(deltaTime, 0f, 0.1f);
            expectedPosition = Vector3.MoveTowards(expectedPosition, target, step);
            physics.Move(expectedPosition, OriginRotation);
            return Vector3.Distance(expectedPosition, target) <= 0.05f;
        }

        // Restores the saved pose and movement state after a controlled return to the origin.
        public void End()
        {
            ValidateActive();
            if (!CanEndAtOrigin)
            {
                throw new InvalidOperationException("Return to the saved origin before ending flight.");
            }
            achievements.Validate();
            physics.Move(Origin, OriginRotation);
            Release(true);
        }

        // Raises the return position when imported terrain or a restored building covers its old height.
        public bool RaiseReturnHeight(float height)
        {
            ValidateActive();
            if (float.IsNaN(height) || float.IsInfinity(height))
            {
                throw new ArgumentException("Invalid return height.");
            }
            if (height <= Origin.y)
            {
                return false;
            }
            Origin = new Vector3(Origin.x, height, Origin.z);
            restoreOriginalMotion = false;
            return true;
        }

        // Requests native emergency return only for the original living, connected player.
        public void Abort()
        {
            if (!Active)
            {
                return;
            }
            if (NativeTransitActive && CanOwnTransitRecovery())
            {
                AbortNativeTransit();
                return;
            }
            try
            {
                if (CanEndAtOrigin && CanRequestEmergencyReturn())
                {
                    Release(true);
                    return;
                }
                if (CanRequestEmergencyReturn())
                {
                    EmergencyReturnRequested = player.TeleportTo(Origin, OriginRotation, true);
                }
            }
            finally
            {
                if (Active)
                {
                    Release(false);
                }
            }
        }

        // Prevents a late airborne arrival after timeout, including the native-completion cooldown frame.
        private void AbortNativeTransit()
        {
            try
            {
                EmergencyReturnRequested = NativeTransitRecovery.Return(player, transitExit, Origin, OriginRotation);
            }
            catch (Exception error)
            {
                expectedPosition = player.transform.position;
                physics.Activate();
                throw new InvalidOperationException("Native transit recovery failed; flight protection remains active. " +
                    "Leave the world and reconnect before resuming from the saved return checkpoint.", error);
            }
            Release(false);
        }

        // Limits private recovery to this exact living local owner in its original connected world.
        private bool CanOwnTransitRecovery()
        {
            return player != null && player == Player.m_localPlayer && !player.IsDead() &&
                network != null && network == ZNet.instance && network.GetWorldUID() == worldUid &&
                ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && view != null &&
                view.IsValid() && view.IsOwner();
        }

        // Rolls back partially acquired physics if a Unity setter fails.
        private void Acquire()
        {
            try
            {
                ClearInput(player);
                physics.Activate();
                Active = true;
                current = this;
            }
            catch
            {
                physics.Restore(true);
                throw;
            }
        }

        // Identifies the controlled player for narrowly scoped Harmony prefixes.
        public static bool IsControlled(Character character)
        {
            return current != null && current.Active && character != null &&
                current.player == character && Player.m_localPlayer == character && !character.IsDead();
        }

        // Clears only a stale actor or disconnected session, retaining protection after a same-session fault.
        public static void ReleaseStaleControl()
        {
            var flight = current;
            if (flight == null || !flight.Active)
            {
                return;
            }
            if (flight.player == null || flight.player != Player.m_localPlayer || flight.player.IsDead() ||
                flight.network == null || flight.network != ZNet.instance || ZNet.World == null ||
                ZNet.World.m_uid != flight.worldUid || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                flight.Release(false);
            }
        }

        // Stops on a new world, lost ownership, death, teleport, or external position correction.
        private void ValidateActive(bool nativeTransit = false)
        {
            if (!Active || player == null || player != Player.m_localPlayer || player.IsDead())
            {
                throw new InvalidOperationException("Controlled flight lost its living local player.");
            }
            if (network == null || network != ZNet.instance || network.GetWorldUID() != worldUid ||
                ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                throw new InvalidOperationException("The connected world changed or disconnected.");
            }
            if (nativeTransit != NativeTransitActive || view == null || !view.IsValid() || !view.IsOwner() ||
                (!nativeTransit && player.IsTeleporting()) ||
                player.IsAttached() || player.InCutscene())
            {
                throw new InvalidOperationException("The player's network or movement state changed.");
            }
            if (!nativeTransit && Vector3.Distance(player.transform.position, expectedPosition) > 3f)
            {
                throw new InvalidOperationException("The player was moved outside crawler control; flight stopped.");
            }
            if (player.GetHealth() <= minimumHealth)
            {
                throw new InvalidOperationException("Health fell below the flight safety threshold; return requested.");
            }
        }

        // Rejects motion states that cannot be restored without interrupting normal gameplay.
        private static void ValidateStart(Player value, bool recovering)
        {
            AchievementGuard.CheckCompatibility();
            if (value == null || value != Player.m_localPlayer || Game.instance == null ||
                ZNet.instance == null || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
            {
                throw new InvalidOperationException("Join a world before starting the crawl.");
            }
            ZNetView localView = value.GetComponent<ZNetView>();
            if (localView == null || !localView.IsValid() || !localView.IsOwner() || value.IsDead() ||
                value.GetHealth() <= 10f ||
                value.IsTeleporting() || value.InCutscene() || value.IsAttached() || value.InBed() ||
                value.InAttack() || value.InDodge() || value.InEmote() || value.m_autoRun ||
                value.IsSwimming() || (!recovering && !value.IsOnGround()) || value.InDebugFlyMode() ||
                value.GetStandingOnShip() != null || (!recovering && value.GetVelocity().sqrMagnitude > 1f))
            {
                throw new InvalidOperationException("Stand still on solid ground, outside combat or attachments.");
            }
        }

        // Always clears the controlled-player marker even if Unity rejects state restoration.
        private void Release(bool atOrigin)
        {
            try
            {
                physics.Restore(atOrigin && restoreOriginalMotion);
            }
            finally
            {
                Active = false;
                NativeTransitActive = false;
                if (current == this)
                {
                    current = null;
                }
                player = null;
                view = null;
                network = null;
                physics = null;
            }
        }

        // Refuses return after death, logout, ownership loss, world change, or another teleport.
        private bool CanRequestEmergencyReturn()
        {
            return player != null && player == Player.m_localPlayer && !player.IsDead() &&
                network != null && network == ZNet.instance && network.GetWorldUID() == worldUid &&
                ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected && view != null &&
                view.IsValid() && view.IsOwner() && !player.IsTeleporting() && !player.InCutscene() &&
                !player.IsAttached();
        }

        // Clears input before control is acquired without changing progression or debug settings.
        private static void ClearInput(Player value)
        {
            value.SetControls(Vector3.zero, false, false, false, false, false, false,
                false, false, false, false, false);
        }

        // Rejects non-finite coordinates before they reach Unity or the network.
        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        // Rejects malformed persisted rotations before applying them to the player's body.
        private static bool FiniteRotation(Quaternion value)
        {
            float norm = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return !float.IsNaN(norm) && !float.IsInfinity(norm) && Mathf.Abs(norm - 1f) < 0.05f;
        }
    }
}
