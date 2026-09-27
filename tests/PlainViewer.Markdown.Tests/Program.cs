using PlainViewer.Core;

int failed = 0, passed = 0;
void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Test(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); passed++; } catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); failed++; } }
List<ViewRun> Runs(string text) => MarkdownView.Parse(text).SelectMany(b => b.Runs).ToList();
string Text(string text) => string.Concat(Runs(text).Select(r => r.Text));
Test("HTML entities decode to visible text", () => Check(Text("A &amp; B &#x1F600; &lt;tag&gt;") == "A & B 😀 <tag>"));
Test("Decoded entities never become HTML", () => Check(Runs("&lt;script&gt;alert(1)&lt;/script&gt;").All(r => r.Link is null)));
Test("Soft break becomes a space", () => Check(Text("first\nsecond") == "first second"));
Test("Hard break remains a newline", () => Check(Text("first  \nsecond") == "first\nsecond"));
Test("Email autolink has allowed mailto target", () => Check(Runs("<person@example.com>").Single().Link == "mailto:person@example.com"));
Test("Combined emphasis preserves both styles", () => Check(Runs("***both***").Single(r => r.Text == "both") is { Bold: true, Italic: true }));
Test("Inline math is literal code with delimiters", () => Check(Runs("Value $x_1 + \\alpha$.").Any(r => r.Code && r.Text == "$x_1 + \\alpha$")));
Test("Display math preserves literal source", () => { var block = MarkdownView.Parse("$$\nx_1 + \\alpha\n$$").Single(); Check(block.Kind == "code" && block.Text == "$$\nx_1 + \\alpha\n$$"); });
Test("Currency is not lost", () => Check(Text("Price $5 and $10.") == "Price $5 and $10."));
Test("Code keeps entity spelling", () => Check(Runs("`&amp; $x$`").Single().Text == "&amp; $x$"));
Test("Math inside fenced code stays unchanged", () => Check(MarkdownView.Parse("```\n$x$ &amp;\n```").Single().Text == "$x$ &amp;"));
Test("Image descriptions preserve entities without reading targets", () => Check(Text("![A &amp; B](file:///C:/secret)") == "[Image: A & B]"));
Test("Entity-encoded unsafe link stays inert", () => Check(Runs("[bad](javascript&#58;alert)").All(r => r.Link is null)));
Test("Math offsets remain correct after front matter", () => Check(MarkdownView.Parse("---\ntitle: test\n---\n\nMath $x$.").SelectMany(b => b.Runs).Any(r => r.Code && r.Text == "$x$")));
Console.WriteLine($"Markdown regressions: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
