using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Landoria.WorldCrawler.Flight
{
    // Saves and restores only the movement state owned by controlled flight.
    internal sealed class FlightPhysics
    {
        private readonly Rigidbody body;
        private readonly Player player;
        private readonly bool gravity;
        private readonly bool kinematic;
        private readonly bool collisions;
        private readonly RigidbodyInterpolation interpolation;
        private readonly Vector3 velocity;
        private readonly Vector3 angularVelocity;
        private readonly Dictionary<FieldInfo, object> fields = new Dictionary<FieldInfo, object>();

        // Captures physics and fall bookkeeping before any mod-owned change.
        public FlightPhysics(Player player)
        {
            this.player = player;
            body = player.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
            {
                throw new InvalidOperationException("The player's normal rigidbody is not ready.");
            }
            gravity = body.useGravity;
            kinematic = body.isKinematic;
            collisions = body.detectCollisions;
            interpolation = body.interpolation;
            velocity = body.linearVelocity;
            angularVelocity = body.angularVelocity;
            SaveField("m_maxAirAltitude", typeof(float));
            SaveField("m_lastGroundTouch", typeof(float));
            SaveField("m_fallTimer", typeof(float));
            SaveField("m_currentVel", typeof(Vector3));
        }

        // Makes the body movable by the crawler without debug or invulnerability flags.
        public void Activate()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
        }

        // Lets native teleport update velocity and pose while input and ordinary motion remain suspended.
        public void SuspendForNativeTransit()
        {
            body.isKinematic = false;
            body.useGravity = false;
            body.detectCollisions = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        // Moves both physics and render transforms so ordinary network sync can publish them.
        public void Move(Vector3 position, Quaternion rotation)
        {
            body.position = position;
            body.rotation = rotation;
            player.transform.SetPositionAndRotation(position, rotation);
        }

        // Restores saved physics, retaining exact fall bookkeeping only at the original position.
        public void Restore(bool atOrigin)
        {
            if (body == null)
            {
                return;
            }
            body.isKinematic = kinematic;
            body.useGravity = gravity;
            body.detectCollisions = collisions;
            body.interpolation = interpolation;
            body.linearVelocity = atOrigin ? velocity : Vector3.zero;
            body.angularVelocity = atOrigin ? angularVelocity : Vector3.zero;
            if (player != null && !player.IsDead())
            {
                RestoreFields(atOrigin);
            }
        }

        // Requires known field types rather than silently dropping unavailable state.
        private void SaveField(string name, Type type)
        {
            FieldInfo field = AccessTools.Field(typeof(Character), name);
            if (field == null || field.FieldType != type)
            {
                throw new MissingFieldException(typeof(Character).FullName, name);
            }
            fields.Add(field, field.GetValue(player));
        }

        // Prevents an aborted flight from retaining stale airborne distance or momentum.
        private void RestoreFields(bool atOrigin)
        {
            foreach (KeyValuePair<FieldInfo, object> field in fields)
            {
                object value = field.Value;
                if (!atOrigin)
                {
                    value = field.Key.FieldType == typeof(Vector3) ? (object)Vector3.zero : 0f;
                    if (field.Key.Name == "m_maxAirAltitude")
                    {
                        value = player.transform.position.y;
                    }
                    if (field.Key.Name == "m_lastGroundTouch")
                    {
                        value = 1f;
                    }
                }
                field.Key.SetValue(player, value);
            }
        }
    }
}
