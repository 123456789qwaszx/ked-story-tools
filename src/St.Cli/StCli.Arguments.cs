using System.Globalization;
using Ked.Presentation.Core;
using St.Authoring.Editing;

namespace St.Cli;

public static partial class StCli
{
    /// <summary>
    /// 명령 뒤의 인자 — <c>--</c>로 시작하는 것만 선택지다. 값은 <c>-11.52u</c>처럼 빼기표로 시작할 수 있어서
    /// 빼기표 하나짜리는 자리 인자로 읽는다.
    /// </summary>
    private sealed class Arguments
    {
        public List<string> Positional { get; } = [];

        public string? Line { get; private set; }

        public string? Kind { get; private set; }

        public string? Specs { get; private set; }

        public string? Card { get; private set; }

        public int? Index { get; private set; }

        public bool Json { get; private set; }

        public bool DryRun { get; private set; }

        public bool Clear { get; private set; }

        public static Arguments Parse(ReadOnlySpan<string> args)
        {
            Arguments parsed = new();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    parsed.Positional.Add(arg);
                    continue;
                }

                switch (arg)
                {
                    case "--json":
                        parsed.Json = true;
                        break;
                    case "--dry-run":
                        parsed.DryRun = true;
                        break;
                    case "--clear":
                        parsed.Clear = true;
                        break;
                    case "--line":
                        parsed.Line = Value(args, ref i);
                        break;
                    case "--kind":
                        parsed.Kind = Value(args, ref i);
                        break;
                    case "--specs":
                        parsed.Specs = Value(args, ref i);
                        break;
                    case "--card":
                        parsed.Card = Value(args, ref i);
                        break;
                    case "--index":
                        string text = Value(args, ref i);

                        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                        {
                            throw new UsageException($"--index '{text}'가 0 이상의 수가 아니다");
                        }

                        parsed.Index = index;
                        break;
                    default:
                        throw new UsageException($"모르는 선택지 '{arg}'");
                }
            }

            return parsed;
        }

        public void Expect(int count, string usage)
        {
            if (Positional.Count != count)
            {
                throw new UsageException(usage);
            }
        }

        public void ExpectAtLeast(int count, string usage)
        {
            if (Positional.Count < count)
            {
                throw new UsageException(usage);
            }
        }

        public CommandAddress Address(int position)
            => CommandAddress.TryParse(Positional[position], out CommandAddress address, out string error)
                ? address
                : throw new UsageException(error);

        /// <summary><c>--kind a,b</c>. 적지 않았으면 null — 거르지 않는다.</summary>
        public IReadOnlySet<CommandKind>? Kinds()
        {
            if (Kind is null)
            {
                return null;
            }

            HashSet<CommandKind> kinds = [];

            foreach (string key in Kind.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!CommandKinds.TryParse(key, out CommandKind kind))
                {
                    throw new UsageException($"종류 '{key}'를 모른다 — {string.Join(" · ", CommandKinds.All.Select(CommandKinds.Key))}");
                }

                kinds.Add(kind);
            }

            return kinds;
        }

        private static string Value(ReadOnlySpan<string> args, ref int i)
        {
            if (i + 1 >= args.Length)
            {
                throw new UsageException($"{args[i]} 뒤에 값이 없다");
            }

            return args[++i];
        }
    }
}
