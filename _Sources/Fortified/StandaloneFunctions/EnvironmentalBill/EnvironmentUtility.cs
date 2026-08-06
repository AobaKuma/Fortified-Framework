using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Fortified
{
    public static class EnvironmentUtility
    {
        /// <summary>
        /// Shared precondition for every environment probe. A thing that is not spawned on a
        /// map has no meaningful environment, so callers must not treat it as a pass.
        /// </summary>
        private static bool CanProbe(Thing thing)
        {
            return thing != null && !thing.Destroyed && thing.Spawned && thing.Map != null;
        }

        public static AcceptanceReport InMicroGravity(Thing thing)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            if (!ModsConfig.OdysseyActive) Log.WarningOnce($"Warning, {thing} checking Gravity without OdysseyActive.", 123457);

            Tile tileInfo = thing.Map.TileInfo;
            if (tileInfo?.Layer?.Def == null) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            if (tileInfo.Layer.Def == PlanetLayerDefOf.Surface)
            {
                return "FFF.Cannot.TableNotInMicroGravity".Translate();
            }
            return true;
        }

        public static AcceptanceReport InPressureBetween(Thing thing, FloatRange range)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            if (!ModsConfig.OdysseyActive) Log.WarningOnce($"Warning, {thing} checking Pressure without OdysseyActive.", 123457);

            // TrueMin/TrueMax so a def author writing the bounds in either order still gets a sane band.
            float min = range.TrueMin;
            float max = range.TrueMax;
            float vacuum = thing.Position.GetVacuum(thing.Map);
            if (vacuum < min || vacuum > max)
                return "FFF.Cannot.TableNotInPressureBetween".Translate(vacuum.ToStringPercent(), min.ToStringPercent(), max.ToStringPercent());
            return true;
        }

        public static AcceptanceReport InPressure(Thing thing, float requirement = 0.75f)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            if (!ModsConfig.OdysseyActive) Log.WarningOnce($"Warning, {thing} checking Pressure without OdysseyActive.", 123457);

            float vacuum = thing.Position.GetVacuum(thing.Map);
            if (vacuum > requirement)
                return "FFF.Cannot.TableNotInPressure".Translate(vacuum.ToStringPercent(), requirement.ToStringPercent());
            return true;
        }

        public static AcceptanceReport InVacuum(Thing thing, float requirement = 0.25f)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            if (!ModsConfig.OdysseyActive) Log.WarningOnce($"Warning, {thing} checking Vacuum without OdysseyActive.", 123457);

            float vacuum = thing.Position.GetVacuum(thing.Map);
            if (vacuum < requirement)
                return "FFF.Cannot.TableNotInVacuum".Translate(vacuum.ToStringPercent(), requirement.ToStringPercent());
            return true;
        }

        public static AcceptanceReport InLightnessBetween(Thing thing, FloatRange range)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            float min = range.TrueMin;
            float max = range.TrueMax;
            float lightLevel = Mathf.Clamp01(thing.Map.glowGrid.GroundGlowAt(thing.Position));
            if (lightLevel < min || lightLevel > max)
                return "FFF.Cannot.TableNotInLightnessBetween".Translate(lightLevel.ToStringPercent(), min.ToStringPercent(), max.ToStringPercent());
            return true;
        }

        public static AcceptanceReport InLightness(Thing thing, float requirement = 0.75f)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            float lightLevel = Mathf.Clamp01(thing.Map.glowGrid.GroundGlowAt(thing.Position));
            if (lightLevel < requirement)
                return "FFF.Cannot.TableNotInLightness".Translate(lightLevel.ToStringPercent(), requirement.ToStringPercent());
            return true;
        }

        public static AcceptanceReport InDarkness(Thing thing, float requirement = 0.25f)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            float lightLevel = Mathf.Clamp01(thing.Map.glowGrid.GroundGlowAt(thing.Position));
            if (lightLevel > requirement)
                return "FFF.Cannot.TableNotInDarkness".Translate(lightLevel.ToStringPercent(), requirement.ToStringPercent());
            return true;
        }

        public static AcceptanceReport InCleanRoom(Thing thing, float requirement = 0.1f)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            Room room = thing.Position.GetRoom(thing.Map);
            if (room == null) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
            if (cleanliness < requirement)
                return "FFF.Cannot.TableNotInCleanRoom".Translate(cleanliness.ToString("0.##"), requirement.ToString("0.##"));
            return true;
        }

        public static AcceptanceReport InTemperature(Thing thing, FloatRange allowedRange)
        {
            if (!CanProbe(thing)) return "FFF.Cannot.TableEnvironmentUnknown".Translate();

            float min = allowedRange.TrueMin;
            float max = allowedRange.TrueMax;
            float temperature = thing.AmbientTemperature;
            if (temperature < min || temperature > max)
                return "FFF.Cannot.TableNotInTemperatureBetween".Translate(
                    temperature.ToStringTemperature("F0"), min.ToStringTemperature("F0"), max.ToStringTemperature("F0"));
            return true;
        }
    }
}
