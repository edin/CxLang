import { defineConfig } from "astro/config";

const cxLanguage = {
  name: "cx",
  scopeName: "source.cx",
  patterns: [
    { include: "#comments" },
    { include: "#strings" },
    { include: "#numbers" },
    { include: "#keywords" },
    { include: "#types" },
    { include: "#functions" }
  ],
  repository: {
    comments: {
      patterns: [
        {
          name: "comment.line.double-slash.cx",
          match: "//.*$"
        },
        {
          name: "comment.block.cx",
          begin: "/\\*",
          end: "\\*/"
        }
      ]
    },
    strings: {
      patterns: [
        {
          name: "string.quoted.double.cx",
          begin: "\"",
          end: "\"",
          patterns: [{ name: "constant.character.escape.cx", match: "\\\\." }]
        },
        {
          name: "string.quoted.single.cx",
          begin: "'",
          end: "'",
          patterns: [{ name: "constant.character.escape.cx", match: "\\\\." }]
        }
      ]
    },
    numbers: {
      name: "constant.numeric.cx",
      match: "\\b(?:0x[0-9A-Fa-f_]+|\\d[\\d_]*(?:\\.\\d[\\d_]*)?)\\b"
    },
    keywords: {
      name: "keyword.control.cx",
      match:
        "\\b(?:as|case|const|declare|default|else|expose|extension|fn|for|foreach|from|if|import|in|interface|let|match|module|requires|return|static|struct|switch|type|union|using|where|while)\\b"
    },
    types: {
      name: "support.type.cx",
      match:
        "\\b(?:Self|bool|char|double|float|int|i32|i64|opaque|u8|u16|u32|u64|usize|void)\\b"
    },
    functions: {
      name: "entity.name.function.cx",
      match: "\\b[A-Za-z_][A-Za-z0-9_]*(?=\\s*\\()"
    }
  }
};

export default defineConfig({
  site: "https://cxlang.dev",
  markdown: {
    shikiConfig: {
      langs: [cxLanguage]
    }
  }
});
