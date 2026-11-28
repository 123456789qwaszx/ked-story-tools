using System.Text;
using Ked.Presentation.Core;

namespace St.Authoring.Composing;

/// <summary>
/// 닻 하나와 거기 딸린 연출 쪽 줄. 닻은 대사 줄(줄 ID가 있다)이거나 뼈대 줄(<c>&lt;&lt;if&gt;&gt;</c> 갈래 · 선택지 · 줄 ID 없는 대사)이다.
/// </summary>
internal sealed class LayerItem
{
    /// <summary>닻 앞의 연출 쪽 줄 — 빈 줄 · 주석 줄 · 명령. 원문 그대로(줄 끝 문자까지).</summary>
    public string Lead = string.Empty;

    /// <summary>앞의 줄 가운데 명령이 있는가 — 빈 줄 · 주석뿐이면 버려도 잃는 연출이 없다.</summary>
    public bool LeadHasCommand;

    /// <summary>닻의 물리 줄 — 줄 끝 문자까지.</summary>
    public string Raw = string.Empty;

    /// <summary>대사 줄이면 그 가른 꼴, 뼈대 줄이면 null.</summary>
    public LineText? Line;

    /// <summary>뼈대 줄을 맞출 글 — 주석과 양끝 공백을 뺀 것.</summary>
    public string Key = string.Empty;

    public string? LineId => Line?.LineId;
}

/// <summary>노드 하나 — 머리(앞 노드의 <c>===</c> 다음부터 <c>---</c>까지) · 닻들 · 꼬리 · <c>===</c>.</summary>
internal sealed class LayerNode
{
    public string Header = string.Empty;

    public string Title = string.Empty;

    public readonly List<LayerItem> Items = [];

    /// <summary><c>===</c> 앞의 연출 쪽 줄 — 노드의 꼬리 명령과 빈 줄.</summary>
    public string Tail = string.Empty;

    public bool TailHasCommand;

    /// <summary><c>===</c> 줄 — 파일이 노드 안에서 끝났으면 빈 글.</summary>
    public string End = string.Empty;
}

/// <summary>
/// 층의 yarn 파일 하나를 물리 줄로 읽은 꼴(docs/PLAN.md §2.5 I1). 읽은 것을 그대로 다시 쓰면 바이트가 같다 — 합치기는 이 조각을 옮겨 적기만 한다.
/// 갈래는 코어의 줄 읽기(<see cref="YarnLineReader"/>)와 같은 눈으로 가른다: 주석을 벗기고, 노드 밖은 머리, 안에서는
/// 분기(<see cref="YarnCommandTextExtractor.IsYarnControl"/>) · 선택지 · 대사 줄이 닻이고 그 밖은 연출 쪽이다.
/// </summary>
internal sealed class LayerScript
{
    public readonly List<LayerNode> Nodes = [];

    /// <summary>마지막 <c>===</c> 뒤에 남은 줄.</summary>
    public string Trailer = string.Empty;

    public static LayerScript Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        LayerScript script = new();
        StringBuilder pending = new();
        bool pendingHasCommand = false;
        LayerNode? node = null;
        string title = string.Empty;
        int offset = 0;

        while (offset < text.Length)
        {
            int newline = text.IndexOf('\n', offset);
            int next = newline < 0 ? text.Length : newline + 1;
            string raw = text[offset..next];
            offset = next;

            string line = Strip(raw);

            if (node is null)
            {
                pending.Append(raw);

                if (line.StartsWith("title:", StringComparison.Ordinal))
                {
                    title = line["title:".Length..].Trim();
                }
                else if (line == "---")
                {
                    node = new LayerNode { Header = pending.ToString(), Title = title };
                    pending.Clear();
                    title = string.Empty;
                }

                continue;
            }

            if (line == "===")
            {
                node.Tail = pending.ToString();
                node.TailHasCommand = pendingHasCommand;
                node.End = raw;
                script.Nodes.Add(node);
                node = null;
                pending.Clear();
                pendingHasCommand = false;
                continue;
            }

            LineText? dialogue = null;
            bool anchor;

            if (line.Length == 0)
            {
                anchor = false;
            }
            else if (line.StartsWith("<<", StringComparison.Ordinal))
            {
                anchor = YarnCommandTextExtractor.IsYarnControl(CommandName(line));
                pendingHasCommand |= !anchor;
            }
            else if (line.StartsWith("->", StringComparison.Ordinal))
            {
                anchor = true;
            }
            else
            {
                anchor = true;
                LineText parsed = LineText.Parse(raw);
                dialogue = parsed.LineId is null ? null : parsed;
            }

            if (!anchor)
            {
                pending.Append(raw);
                continue;
            }

            node.Items.Add(new LayerItem
            {
                Lead = pending.ToString(),
                LeadHasCommand = pendingHasCommand,
                Raw = raw,
                Line = dialogue,
                Key = line,
            });

            pending.Clear();
            pendingHasCommand = false;
        }

        if (node is null)
        {
            script.Trailer = pending.ToString();
        }
        else
        {
            node.Tail = pending.ToString();
            node.TailHasCommand = pendingHasCommand;
            script.Nodes.Add(node);
        }

        return script;
    }

    public string ToText()
    {
        StringBuilder text = new();

        foreach (LayerNode node in Nodes)
        {
            text.Append(node.Header);

            foreach (LayerItem item in node.Items)
            {
                text.Append(item.Lead).Append(item.Raw);
            }

            text.Append(node.Tail).Append(node.End);
        }

        return text.Append(Trailer).ToString();
    }

    // 줄 읽기가 보는 글 — 줄 끝 문자와 주석을 벗기고 양끝을 다듬는다.
    private static string Strip(string raw)
    {
        int comment = raw.IndexOf("//", StringComparison.Ordinal);
        return (comment < 0 ? raw : raw[..comment]).Trim().TrimStart('﻿');
    }

    // <<이름 …>>의 이름.
    private static string CommandName(string line)
    {
        int start = 2;

        while (start < line.Length && char.IsWhiteSpace(line[start]))
        {
            start++;
        }

        int end = start;

        while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '>')
        {
            end++;
        }

        return line[start..end];
    }
}
