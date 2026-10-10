namespace HultUI.Syntax;

/// <summary>
/// Every file extension TextMateSharp's bundled languages list, by language — the extensions
/// <c>RegistryOptions.GetScopeByExtension</c> finds a grammar for. It ignores case, and so does
/// this set. <see cref="SyntaxHighlighter.CanHighlight"/> answers from it, so asking whether a
/// file is code does not load the registry: building that parses every grammar package's JSON,
/// 4.6–5.3 MB and 7–10 ms on the UI thread the first time a file list showed a code file
/// (plan 17). The highlighter still loads the registry for its first tokenize, which apps run
/// off the UI thread.
///
/// <para>Taken from TextMateSharp.Grammars 2.0.4. <c>SyntaxHighlightingTests</c> holds it to
/// what the registry answers for every extension the registry lists, so a TextMateSharp update
/// that adds or drops a language fails there: copy the new lists in from the failure.</para>
/// </summary>
internal static class BundledGrammarExtensions
{
    internal static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        // asciidoc
        ".adoc",
        // bat
        ".bat", ".cmd",
        // clojure
        ".clj", ".cljs", ".cljc", ".cljx", ".clojure", ".edn",
        // coffeescript
        ".coffee", ".cson", ".iced",
        // c
        ".c", ".i",
        // cpp
        ".cpp", ".cc", ".cxx", ".c++", ".hpp", ".hh", ".hxx", ".h++", ".h", ".ii", ".ino", ".inl", ".ipp",
        ".ixx", ".tpp", ".txx", ".hpp.in", ".h.in",
        // cuda-cpp
        ".cu", ".cuh",
        // csharp
        ".cs", ".csx", ".cake",
        // css
        ".css",
        // dart
        ".dart",
        // diff
        ".diff", ".patch", ".rej",
        // dockerfile
        ".dockerfile", ".containerfile",
        // fsharp
        ".fs", ".fsi", ".fsx", ".fsscript",
        // ignore
        ".gitignore_global", ".gitignore",
        // go
        ".go",
        // groovy
        ".groovy", ".gvy", ".gradle", ".jenkinsfile", ".nf",
        // handlebars
        ".handlebars", ".hbs", ".hjs",
        // hlsl
        ".hlsl", ".hlsli", ".fx", ".fxh", ".vsh", ".psh", ".cginc", ".compute",
        // html
        ".html", ".htm", ".shtml", ".xhtml", ".xht", ".mdoc", ".jsp", ".asp", ".aspx", ".jshtm", ".volt",
        ".ejs", ".rhtml",
        // ini
        ".ini",
        // properties
        ".properties", ".cfg", ".conf", ".directory", ".gitattributes", ".gitconfig", ".gitmodules",
        ".editorconfig",
        // java
        ".java", ".jav",
        // javascriptreact
        ".jsx",
        // javascript
        ".js", ".es6", ".mjs", ".cjs", ".pac",
        // json
        ".json", ".bowerrc", ".jscsrc", ".webmanifest", ".js.map", ".css.map", ".ts.map", ".har",
        ".jslintrc", ".jsonld", ".geojson", ".ipynb",
        // jsonc
        ".jsonc", ".eslintrc", ".eslintrc.json", ".jsfmtrc", ".jshintrc", ".swcrc", ".hintrc", ".babelrc",
        // julia
        ".jl",
        // tex
        ".sty", ".cls", ".bbx", ".cbx",
        // latex
        ".tex", ".ltx", ".ctx",
        // bibtex
        ".bib",
        // less
        ".less",
        // log
        ".log", "*.log.?",
        // lua
        ".lua",
        // makefile
        ".mak", ".mk",
        // markdown
        ".md", ".mkd", ".mdwn", ".mdown", ".markdown", ".markdn", ".mdtxt", ".mdtext", ".workbook",
        // objective-c
        ".m",
        // objective-cpp
        ".mm",
        // pascal
        ".pas", ".pp",
        // perl
        ".pl", ".pm", ".pod", ".t", ".psgi",
        // perl6
        ".p6", ".pl6", ".pm6", ".nqp",
        // php
        ".php", ".php4", ".php5", ".phtml", ".ctp",
        // powershell
        ".ps1", ".psm1", ".psd1", ".pssc", ".psrc",
        // jade
        ".pug", ".jade",
        // python
        ".py", ".rpy", ".pyw", ".cpy", ".gyp", ".gypi", ".pyi", ".ipy", ".pyt",
        // r
        ".r", ".rhistory", ".rprofile", ".rt",
        // razor
        ".cshtml", ".razor",
        // ruby
        ".rb", ".rbx", ".rjs", ".gemspec", ".rake", ".ru", ".erb", ".podspec", ".rbi",
        // rust
        ".rs",
        // scss
        ".scss",
        // shaderlab
        ".shader",
        // shellscript
        ".sh", ".bash", ".bashrc", ".bash_aliases", ".bash_profile", ".bash_login", ".ebuild", ".profile",
        ".bash_logout", ".xprofile", ".xsession", ".xsessionrc", ".zsh", ".zshrc", ".zprofile", ".zlogin",
        ".zlogout", ".zshenv", ".zsh-theme", ".ksh", ".csh", ".cshrc", ".tcshrc", ".yashrc", ".yash_profile",
        // sql
        ".sql", ".dsql",
        // swift
        ".swift",
        // typescript
        ".ts",
        // typescriptreact
        ".tsx",
        // typst
        ".typ",
        // typst-code
        ".typc",
        // vb
        ".vb", ".brs", ".vbs", ".bas",
        // x86asm
        ".asm",
        // xml
        ".xml", ".xsd", ".ascx", ".atom", ".axml", ".axaml", ".bpmn", ".cpt", ".csl", ".csproj",
        ".csproj.user", ".dita", ".ditamap", ".dtd", ".ent", ".mod", ".dtml", ".fsproj", ".fxml", ".iml",
        ".isml", ".jmx", ".launch", ".menu", ".mxml", ".nuspec", ".opml", ".owl", ".proj", ".props", ".pt",
        ".publishsettings", ".pubxml", ".pubxml.user", ".rbxlx", ".rbxmx", ".rdf", ".rng", ".rss", ".shproj",
        ".storyboard", ".svg", ".targets", ".tld", ".tmx", ".vbproj", ".vbproj.user", ".vcxproj",
        ".vcxproj.filters", ".wsdl", ".wxi", ".wxl", ".wxs", ".xaml", ".xbl", ".xib", ".xlf", ".xliff",
        ".xpdl", ".xul", ".xoml",
        // xsl
        ".xsl", ".xslt",
        // yaml
        ".yml", ".eyaml", ".eyml", ".yaml",
    };
}
