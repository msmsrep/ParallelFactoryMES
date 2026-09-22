"""画面の日本語リテラルを L[...] で包む半自動変換（Spec.md 7.9。docs-dev/I18nPlan.md の I18N-05〜08 用）。
使い方:
  python scripts/I18nWrap.py apply <files...>  … 変換して書き戻し、@code 側の変換（code）と包めなかった行（LEFT）を報告
  python scripts/I18nWrap.py keys <files...>   … en.resx に無いキーを scripts/keys.txt に出す
  python scripts/I18nWrap.py resx <file>       … 「原文 => 訳」の行を en.resx に追記（既存キーは飛ばす）
変換後は必ず報告と差分を見直す。保存される値（単位の既定値「個」など）は、行末に「// …訳さない」と書けば以後も触らない。
行末の空白は編集の途中で落ちやすいので、resx に足した後で確認する（"、" => ", " など）。
"""
import os, re, sys
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
WEB = os.path.join(HERE, "..", "src", "MesApp.Client.Web")
RESX = os.path.join(WEB, "Shared", "UiText.en.resx")
# ひらがな・カタカナ・漢字・全角記号・半角カナ
JP = re.compile(r"[぀-ヿ一-鿿！-ﾟ]")
ATTRS = {"Label", "KeyLabel", "EmptyLabel", "Placeholder", "placeholder", "title", "SubmitLabel",
         "AriaLabel", "aria-label", "EmptyText", "DeltaLabel", "Title", "Name", "Unit", "BarUnit", "LineUnit", "Text", "Caption"}

def read(p):
    raw = open(p, "rb").read()
    return raw.decode("utf-8-sig").replace("\r\n", "\n"), raw.startswith(b"\xef\xbb\xbf"), b"\r\n" in raw

def write(p, t, bom, crlf):
    if crlf: t = t.replace("\n", "\r\n")
    open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + t.encode("utf-8"))

def interp_to_L(body):
    """$"...{a}...{b:fmt}..." の本体 → ('...{0}...{1:fmt}...', ['a','b'])。入れ子の括弧・文字列は簡易対応"""
    out, args, i = "", [], 0
    while i < len(body):
        c = body[i]
        if c == "{" and body[i:i+2] == "{{": out += "{{"; i += 2; continue
        if c == "}" and body[i:i+2] == "}}": out += "}}"; i += 2; continue
        if c == "{":
            depth, j, inq = 1, i + 1, False
            while j < len(body) and depth:
                ch = body[j]
                if ch == '"': inq = not inq
                elif not inq and ch in "({[": depth += 1
                elif not inq and ch in ")}]": depth -= 1
                j += 1
            expr = body[i+1:j-1]
            fmt = ""
            # 書式指定（:yyyy-MM-dd 等）。三項演算子の ':' と区別するため、括弧の外で最後の ':' の後ろが英数記号のみのとき
            m = re.match(r"^(.*?[^:\s])\s*:([A-Za-z0-9#.,\-/ ]+)$", expr)
            if m and "?" not in m.group(1): expr, fmt = m.group(1), ":" + m.group(2)
            out += "{%d%s}" % (len(args), fmt); args.append(expr.strip()); i = j; continue
        out += c; i += 1
    return out, args

LIT = re.compile(r'(\$?)"((?:[^"\\\n]|\\.)*)"')

def wrap_literal(m, text, in_markup):
    """1つの文字列リテラルの置換結果。None なら変えない"""
    dollar, body = m.group(1), m.group(2)
    if not JP.search(body): return None
    before = text[:m.start()]
    if before.endswith("L["): return None
    if dollar:
        fmt, args = interp_to_L(body)
        repl = 'L["%s"%s]' % (fmt, "".join(", " + a for a in args))
    else:
        repl = 'L["%s"]' % body
    am = re.search(r'([\w\-@:]+)=$', before)
    if in_markup and am:
        if am.group(1) in ATTRS and not dollar and "@" not in body:
            return '"@L["%s"]"' % body
        return None  # 知らない属性は報告に回す
    return repl

def strip_comment(line):
    """行末の // コメントを分ける（文字列の外のものだけ）"""
    inq = False
    for i, c in enumerate(line):
        if c == '"' and (i == 0 or line[i-1] != "\\"): inq = not inq
        if not inq and line[i:i+2] == "//" and (i == 0 or line[i-1] in " \t;{"): return line[:i], line[i:]
    return line, ""

def convert(text):
    lines = text.split("\n")
    out, report, in_code, in_cmt = [], [], False, False
    for no, line in enumerate(lines, 1):
        s = line.strip()
        if in_cmt:
            out.append(line); in_cmt = "*@" not in line; continue
        if s.startswith("@*") and "*@" not in s:
            out.append(line); in_cmt = True; continue
        if s.startswith("@code") or s.startswith("@functions"): in_code = True
        # 「訳さない」とコメントした行（保存される値など）は触らない
        if s.startswith(("//", "///", "@*", "*", "@page", "@using", "@inject")) or not JP.search(line) or "訳さない" in line:
            out.append(line); continue
        # 行内の @* *@ は保護
        protected = {}
        def prot(m):
            k = "\x00%d\x00" % len(protected); protected[k] = m.group(0); return k
        work = re.sub(r"@\*.*?\*@", prot, line)
        body, cmt = strip_comment(work)
        new = body
        # 文字列リテラル
        res, last = "", 0
        for m in LIT.finditer(new):
            r = wrap_literal(m, new, not in_code)
            if r is not None:
                res += new[last:m.start()] + r; last = m.end()
        new = res + new[last:]
        if not in_code:
            # >テキスト</ または >テキスト<タグ
            new = re.sub(r'>([^<>@"{}]*?' + JP.pattern + r'[^<>@"{}]*?)<',
                         lambda m: ">" + (lambda t: m.group(1).replace(t, '@L["%s"]' % t))(m.group(1).strip()) + "<", new)
            # 行全体がテキスト
            st = new.strip()
            if JP.search(st) and not re.search(r'[<>@"{}=;]', st):
                new = new.replace(st, '@L["%s"]' % st)
        new = new + cmt
        for k, v in protected.items(): new = new.replace(k, v)
        if new != line and in_code: report.append(("code", no, new.strip()))
        rest = re.sub(r'L\["(?:[^"\\]|\\.)*"', "", re.sub(r"@\*.*?\*@", "", strip_comment(new)[0]))
        if JP.search(rest): report.append(("LEFT", no, new.strip()))
        out.append(new)
    return "\n".join(out), report

def keys_in(text):
    return [m.group(1) for m in re.finditer(r'L\["((?:[^"\\]|\\.)*)"', text)]

def existing_keys():
    t, _, _ = read(RESX)
    return {k.replace("&quot;", '"').replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">")
            for k in re.findall(r'<data name="([^"]*)"', t)}

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    cmd, args = sys.argv[1], sys.argv[2:]
    if cmd == "apply":
        for p in args:
            t, bom, crlf = read(p)
            n, rep = convert(t)
            if n != t: write(p, n, bom, crlf)
            for kind, no, s in rep: print(f"{kind} {os.path.basename(p)}:{no}: {s}")
    elif cmd == "keys":
        have, seen, out = existing_keys(), set(), []
        for p in args:
            for k in keys_in(read(p)[0]):
                if k not in have and k not in seen: seen.add(k); out.append(k)
        open(os.path.join(HERE, "keys.txt"), "w", encoding="utf-8").write("\n".join(out) + "\n")
        print(len(out), "keys ->", os.path.join(HERE, "keys.txt"))
    elif cmd == "resx":
        have = existing_keys()
        t, bom, crlf = read(RESX)
        add, n = "", 0
        for line in open(args[0], encoding="utf-8"):
            line = line.rstrip("\n")
            if not line.strip(): continue
            k, v = line.split(" => ", 1)
            if k in have: continue
            have.add(k); n += 1
            add += '  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>\n' % (
                escape(k, {'"': "&quot;"}), escape(v))
        write(RESX, t.replace("</root>", add + "</root>"), bom, crlf)
        print("added", n)
