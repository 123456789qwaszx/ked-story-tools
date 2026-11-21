using Ked.Presentation.Core;

namespace St.Authoring.Motions;

/// <summary>
/// 프레임마다 찍힌 키를 솎는다 — 원본에서 뽑아 구운 모션은 키가 프레임 수만큼이라 손으로 고칠 수 없다(docs/PLAN.md §4.8).
/// 양 끝의 키에서 시작해, 남긴 키로 그린 커브가 원래 키의 값에서 가장 멀리 벗어난 자리의 키를 하나씩 되살린다 —
/// 벗어남이 한도 안에 들 때까지. 남는 키는 원래의 키 그대로다(때 · 값 · 탄젠트를 새로 짓지 않는다).
/// 값은 코어의 <see cref="CurveFunctions"/>로 셈한다 — 런타임이 재생하는 그 식이다.
/// </summary>
public static class CurveSimplifier
{
    /// <summary>그 종류의 커브에서 눈에 띄지 않는 벗어남 — 위치 0.5px · 크기 0.002배 · 회전 0.1°.</summary>
    public static float ToleranceFor(MotionChannelKind kind) => kind switch
    {
        MotionChannelKind.Position => 0.5f,
        MotionChannelKind.Scale => 0.002f,
        _ => 0.1f,
    };

    /// <summary>남길 키의 순번 — 오름차순. 키가 둘 이하면 그대로다.</summary>
    public static IReadOnlyList<int> Keep(CurveKey[] keys, float tolerance)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Length <= 2)
        {
            return Enumerable.Range(0, keys.Length).ToList();
        }

        SortedSet<int> kept = [0, keys.Length - 1];

        while (kept.Count < keys.Length)
        {
            CurveKey[] curve = kept.Select(i => keys[i]).ToArray();
            int worst = -1;
            float error = tolerance;

            for (int i = 1; i < keys.Length - 1; i++)
            {
                float off = kept.Contains(i) ? 0f : Math.Abs(CurveFunctions.Evaluate(curve, keys[i].Time) - keys[i].Value);

                if (off > error)
                {
                    (worst, error) = (i, off);
                }
            }

            if (worst < 0)
            {
                break;
            }

            kept.Add(worst);
        }

        return kept.ToList();
    }
}
