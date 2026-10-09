using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Fortified
{
    /// <summary>
    /// 弧形掃射 Verb 共用的路徑生成 (原版 Verb_ArcSprayProjectile 與 CE 版 Verb_ArcSprayProjectileCE 共用)。
    ///
    /// 軌跡是之字形：從中軸點 (目標格) 出發，先掃到一側邊緣，再於左右邊緣之間來回掃
    /// (縱深由近端逐段推到遠端)，最後回到中軸點收尾。左右範圍 = sprayWidth，
    /// 縱深範圍 = sprayThicknessCells / 2 + sprayArching，與原版 Verb_ArcSpray 的散布範圍一致。
    /// 各發沿折線等距分布，所以路徑長度 = 連發數，不論連發數多少整條之字形都會完整走完，
    /// 也不再需要把 sprayNumExtraCells 設得大於等於射擊次數。
    ///
    /// 近距離保護：目標離砲口很近時，原本的範圍會涵蓋到砲塔旁邊甚至背後的格子，
    /// 槍口會跟著亂轉、子彈往自己人亂飛。所以
    ///   - 近端縱深不會比最小安全距離更靠近砲口，
    ///   - 左右掃幅依距離限制在最大夾角內 (越近掃得越窄)，
    ///   - 取整後仍落在最小射程內的格子改打中軸點。
    /// 遠距離時這些限制都不會生效，軌跡與 sprayWidth / 縱深設定完全相同。
    /// </summary>
    public static class ArcSprayPathUtility
    {
        // 左右邊緣之間來回掃的趟數 (不含出發與收尾那兩段)
        public const int DefaultSweeps = 3;

        // 掃射點相對砲口方向的最大左右夾角 (度)
        public const float DefaultMaxHalfAngle = 30f;

        /// <summary>
        /// 掃射格離砲口中心的最小距離：最小射程 + 取整到格子時的緩衝，且至少要在施放者本體之外
        /// </summary>
        public static float DefaultMinCellDistance(Thing caster, VerbProperties props)
        {
            float bodyRadius = 0.5f * Mathf.Max(caster.def.size.x, caster.def.size.z);
            return Mathf.Max(props.minRange + 0.75f, bodyRadius + 1f);
        }

        /// <summary>
        /// 重新產生掃射路徑，結果寫進 path：共 shots 格，第一發與最後一發都落在中軸點 (target.Cell)
        /// </summary>
        public static void BuildZigzagPath(List<IntVec3> path, Thing caster, LocalTargetInfo target, VerbProperties props,
            int shots, int sweeps, float maxHalfAngle, float minCellDistance)
        {
            path.Clear();
            shots = Mathf.Max(shots, 1);

            Vector3 center = target.CenterVector3;
            Vector3 toTarget = (center - caster.Position.ToVector3Shifted()).Yto0();
            float distance = toTarget.magnitude; // 砲口到中軸點的距離
            // axis = 砲口朝目標的方向 (縱深軸)，tan = 與它垂直的左右軸
            Vector3 axis = toTarget.normalized;
            Vector3 tan = axis.RotatedBy(90f);

            float depthFar = props.sprayThicknessCells * 0.5f + props.sprayArching;
            // 近端縱深只能延伸到「最小安全距離」為止，目標貼近砲口時就完全不往回掃
            float depthNear = Mathf.Clamp(distance - minCellDistance, 0f, depthFar);
            List<Vector2> waypoints = BuildZigzagWaypoints(props.sprayWidth, depthNear, depthFar, sweeps);
            float tanLimit = Mathf.Tan(maxHalfAngle * Mathf.Deg2Rad);

            for (int i = 0; i < shots; i++)
            {
                IntVec3 cell;
                if (i == 0 || i == shots - 1)
                {
                    cell = target.Cell;
                }
                else
                {
                    Vector2 offset = PointAlongPolyline(waypoints, (float)i / (shots - 1));
                    // 該點在縱深軸上離砲口多遠，左右偏移不得超過這個距離對應的夾角
                    float reach = distance + offset.y;
                    float lateral = Mathf.Clamp(offset.x, -reach * tanLimit, reach * tanLimit);
                    cell = (center + lateral * tan + offset.y * axis).ToIntVec3().ClampInsideMap(caster.Map);
                    if ((cell - caster.Position).LengthHorizontalSquared < props.minRange * props.minRange)
                    {
                        // 取整後掉進最小射程內，改打中軸點
                        cell = target.Cell;
                    }
                }
                path.Add(cell);
            }
        }

        // 折線頂點 (x = 左右偏移, y = 縱深偏移，皆以中軸點為原點)：
        // 中軸點 → 第一側邊緣近端 → 左右來回 sweeps 趟、縱深由近推到遠 → 回到中軸點
        private static List<Vector2> BuildZigzagWaypoints(float halfWidth, float depthNear, float depthFar, int sweeps)
        {
            sweeps = Mathf.Max(sweeps, 1);
            float side = Rand.Bool ? 1f : -1f; // 先往哪一側隨機，避免每次軌跡都一樣
            List<Vector2> points = new List<Vector2>(sweeps + 3)
            {
                Vector2.zero,
                new Vector2(side * halfWidth, -depthNear)
            };
            for (int i = 1; i <= sweeps; i++)
            {
                side = -side;
                points.Add(new Vector2(side * halfWidth, Mathf.Lerp(-depthNear, depthFar, (float)i / sweeps)));
            }
            points.Add(Vector2.zero);
            return points;
        }

        // 取折線上「總長度 × progress」處的點 (progress 0~1)，各段按長度比例分配所以射擊間距均勻
        private static Vector2 PointAlongPolyline(List<Vector2> points, float progress)
        {
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                total += Vector2.Distance(points[i - 1], points[i]);
            }
            if (total <= 0f)
            {
                return points[0];
            }
            float remaining = total * Mathf.Clamp01(progress);
            for (int i = 1; i < points.Count; i++)
            {
                float segment = Vector2.Distance(points[i - 1], points[i]);
                if (remaining <= segment)
                {
                    return segment > 0f ? Vector2.Lerp(points[i - 1], points[i], remaining / segment) : points[i];
                }
                remaining -= segment;
            }
            return points[points.Count - 1];
        }
    }
}
