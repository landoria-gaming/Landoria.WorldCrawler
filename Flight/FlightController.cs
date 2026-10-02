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
            expectedPosition = player.transform.position;
            nextAudit = 0f;
            minimumHealth = 0f;
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
                achievements.Validate(Manual);
                nextAudit = Time.unscaledTime + 1f;
            }
            float step = Mathf.Clamp(Speed, 1f, 100f) * Mathf.Clamp(deltaTime, 0f, 0.1f);
            expectedPosition = Vector3.MoveTowards(expectedPosition, target, step);
            physics.Move(expectedPosition, OriginRotation);
            return Vector3.Distance(expectedPosition, target) <= 0.05f;
        }

        // Releases flight at the current position without moving or teleporting to its origin.
        public void End()
        {
            ValidateActive();
            achievements.Validate(Manual);
            Release(false);
        }

        // Releases owned physics in place, including failures and plugin unloads.
        public void Abort()
        {
            if (Active)
            {
                Release(false);
            }
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

        // Suppresses native repositioning only while this connected local body is under flight control.
        internal static bool OwnsFlightMotion(Character character)
        {
            return IsControlled(character) && !current._manualTeleport &&
                current.CanOwnTransitRecovery() && !current.player.IsTeleporting() &&
                !current.player.IsAttached() && !current.player.InCutscene();
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
        private void ValidateActive()
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
            if (view == null || !view.IsValid() || !view.IsOwner() ||
                player.IsTeleporting() ||
                player.IsAttached() || player.InCutscene())
            {
                throw new InvalidOperationException("The player's network or movement state changed.");
            }
            if (Vector3.Distance(player.transform.position, expectedPosition) > 3f)
            {
                throw new InvalidOperationException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The player was moved outside crawler control; flight stopped. Expected={0}; actual={1}; delta={2:F2}m.",
                    expectedPosition.ToString("F2"), player.transform.position.ToString("F2"),
                    Vector3.Distance(player.transform.position, expectedPosition)));
            }
            if (player.GetHealth() <= minimumHealth)
            {
                throw new InvalidOperationException("Health fell below the flight safety threshold; flight stopped.");
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
            string blocker = FindStartBlocker(value, localView, recovering);
            if (blocker != null)
            {
                throw new InvalidOperationException("Cannot start controlled flight: " + blocker + ".");
            }
        }

        // Returns the exact unsafe state that prevents controlled flight from starting.
        private static string FindStartBlocker(Player value, ZNetView localView, bool recovering)
        {
            if (localView == null || !localView.IsValid() || !localView.IsOwner())
            {
                return "the local player does not own its network object";
            }
            if (value.IsDead() || value.GetHealth() <= 0f)
            {
                return "the player is dead";
            }
            if (value.IsTeleporting() || value.InCutscene())
            {
                return "a teleport or cutscene is active";
            }
            if (value.IsAttached() || value.InBed() || value.GetStandingOnShip() != null)
            {
                return "the player is attached, in bed, or on a ship";
            }
            if (value.InAttack() || value.InDodge() || value.InEmote())
            {
                return "an attack, dodge, or emote is active";
            }
            if (value.IsSwimming() || value.InDebugFlyMode())
            {
                return "swimming or Valheim debug flight is active";
            }
            if (value.m_autoRun)
            {
                return "auto-run is active";
            }
            if (!recovering && (!value.IsOnGround() || value.GetVelocity().sqrMagnitude > 1f))
            {
                return "the player is airborne or still moving";
            }
            return null;
        }

        // Always clears the controlled-player marker even if Unity rejects state restoration.
        private void Release(bool atOrigin)
        {
            try
            {
                physics.Restore(false);
            }
            finally
            {
                Active = false;
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
