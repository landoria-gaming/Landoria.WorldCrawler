using System;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Groups transit behavior without changing the controller's shared state.
    internal sealed partial class FlightController
    {
        // Invokes a physically reached native portal without replacing its inventory or world-rule checks.
        public bool BeginPortalTransit(TeleportWorld portal, Vector3 expectedExit)
        {
            ValidateActive();
            if (portal == null || !portal.isActiveAndEnabled || !Finite(expectedExit) ||
                Vector3.Distance(player.transform.position, portal.transform.position) > portal.m_activationRange)
            {
                throw new InvalidOperationException("Reach the loaded portal before requesting native passage.");
            }
            return BeginNativeTransit(expectedExit, () =>
{
    portal.Teleport(player);
    return player.IsTeleporting();
}
);
        }

        // Uses the native loading transition for an explicitly authorized, known coordinate destination.
        public bool BeginCoordinateTransit(Vector3 target)
        {
            ValidateActive();
            if (!Finite(target) || Mathf.Abs(target.x) > 20000f || Mathf.Abs(target.z) > 20000f ||
                Mathf.Abs(target.y) > 20000f)
            {
                throw new ArgumentException("The coordinate jump destination is outside the supported world bounds.");
            }
            return BeginNativeTransit(target, () => player.TeleportTo(target, OriginRotation, true));
        }

        // Records ownership before suspending crawler motion for one accepted native transition.
        private bool BeginNativeTransit(Vector3 target, Func<bool> start)
        {
            NativeTransitRecovery.Validate();
            achievements.Validate();
            transitFrom = player.transform.position;
            transitExit = target;
            NativeTransitSucceeded = false;
            if (!start())
            {
                return false;
            }
            NativeTransitActive = true;
            transitStarted = Time.time;
            physics.SuspendForNativeTransit();
            return true;
        }

        // Waits for native completion and reacquires motion only at the expected endpoint or source.
        public bool TickNativeTransit()
        {
            if (!NativeTransitActive)
            {
                throw new InvalidOperationException("No crawler native transition is active.");
            }
            ValidateActive(true);
            achievements.Validate();
            var position = player.transform.position;
            var atSource = Vector3.Distance(position, transitFrom) <= 4f;
            var atExit = Vector2.Distance(new Vector2(position.x, position.z),
                new Vector2(transitExit.x, transitExit.z)) <= 8f && Mathf.Abs(position.y - transitExit.y) <= 1000f;
            if (!atSource && !atExit)
            {
                throw new InvalidOperationException("Native travel moved outside its verified endpoints.");
            }
            if (player.IsTeleporting())
            {
                if (Time.time - transitStarted > 120f)
                {
                    throw new TimeoutException("The native transition did not finish.");
                }
                return false;
            }
            NativeTransitSucceeded = atExit;
            restoreOriginalMotion = false;
            expectedPosition = position;
            physics.Activate();
            physics.Move(expectedPosition, OriginRotation);
            NativeTransitActive = false;
            return true;
        }
    }
}
