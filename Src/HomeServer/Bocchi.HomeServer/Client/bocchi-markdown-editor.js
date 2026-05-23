import { minimalSetup } from "codemirror";
import { markdown } from "@codemirror/lang-markdown";
import { EditorState } from "@codemirror/state";
import { EditorView, keymap, placeholder as editorPlaceholder } from "@codemirror/view";
import { indentWithTab } from "@codemirror/commands";

const editorByRoot = new WeakMap();

const bocchiTheme = EditorView.theme({
  "&": {
    minHeight: "100%",
    background: "transparent",
    color: "var(--bocchi-text)",
  },
  ".cm-scroller": {
    fontFamily: '"SFMono-Regular", Consolas, "Liberation Mono", monospace',
    fontSize: "0.95rem",
    lineHeight: "1.72",
  },
  ".cm-content": {
    minHeight: "31rem",
    padding: "1.1rem",
  },
  ".cm-line": {
    padding: "0 0.12rem",
  },
  ".cm-gutters": {
    border: "0",
    backgroundColor: "transparent",
    color: "var(--bocchi-text-subtle)",
  },
  ".cm-activeLine, .cm-activeLineGutter": {
    backgroundColor: "color-mix(in srgb, var(--bocchi-blue) 9%, transparent)",
  },
  ".cm-selectionBackground": {
    backgroundColor: "color-mix(in srgb, var(--bocchi-blue) 30%, transparent) !important",
  },
  ".cm-placeholder": {
    color: "var(--bocchi-text-subtle)",
  },
  "&.cm-focused": {
    outline: "none",
  },
  "&.cm-focused .cm-scroller": {
    boxShadow: "inset var(--bocchi-focus-ring)",
  },
  ".cm-cursor": {
    borderLeftColor: "var(--bocchi-action)",
  },
});

function getEditor(root) {
  return editorByRoot.get(root)?.view;
}

function getHost(root) {
  return root?.querySelector("[data-bocchi-codemirror-host]");
}

function normalizeViewMode(value) {
  return value === "preview" || value === "split" || value === "write" ? value : "write";
}

function setRootViewMode(root, mode) {
  if (root) {
    root.dataset.view = normalizeViewMode(mode);
  }
}

function createInsertText(action, selected, snippets = {}) {
  const text = selected || "";
  switch (action) {
    case "heading":
      return prefixLines(text || snippets.heading || "Heading", "## ");
    case "bold":
      return wrap(text, "**", "**", snippets.bold || "bold text");
    case "italic":
      return wrap(text, "_", "_", snippets.italic || "italic text");
    case "quote":
      return prefixLines(text || snippets.quote || "Quoted text", "> ");
    case "list":
      return prefixLines(text || snippets.list || "List item", "- ");
    case "ordered":
      return prefixLines(text || snippets.ordered || "List item", "1. ");
    case "code":
      return fencedBlock(text || "code");
    case "link":
      return wrap(text, "[", "](https://example.com)", snippets.link || "Link text");
    case "image":
      return wrap(text, "![", "](assets/image.png)", snippets.image || "Image description");
    case "formula":
      return `\n$$\n${text || "E = mc^2"}\n$$\n`;
    case "video":
      return `\n@[video](${text || "https://example.com/video"})\n`;
    case "link-card":
      return `\n@[card](${text || "https://example.com"})\n`;
    default:
      return text;
  }
}

function wrap(selected, left, right, placeholder) {
  const value = selected || placeholder;
  return `${left}${value}${right}`;
}

function prefixLines(selected, prefix) {
  return selected
    .split("\n")
    .map((line) => (line.startsWith(prefix) ? line : `${prefix}${line}`))
    .join("\n");
}

function fencedBlock(selected) {
  return `\n\`\`\`\n${selected}\n\`\`\`\n`;
}

function imageMarkdownFromFiles(files) {
  return files
    .filter((file) => file.type?.startsWith("image/"))
    .map((file, index) => {
      const fallbackName = `pasted-image-${index + 1}.png`;
      const safeName = sanitizeAssetName(file.name || fallbackName);
      return `![${safeName}](assets/${safeName})`;
    })
    .join("\n");
}

function sanitizeAssetName(name) {
  return name
    .trim()
    .replace(/[^\w.-]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .toLowerCase() || "pasted-image.png";
}

function replaceSelection(view, text) {
  const selection = view.state.selection.main;
  view.dispatch({
    changes: { from: selection.from, to: selection.to, insert: text },
    selection: { anchor: selection.from + text.length },
    scrollIntoView: true,
  });
  view.focus();
}

function buildPasteAndDropHandlers() {
  return EditorView.domEventHandlers({
    paste(event, view) {
      const markdownText = imageMarkdownFromFiles(Array.from(event.clipboardData?.files ?? []));
      if (!markdownText) {
        return false;
      }

      event.preventDefault();
      replaceSelection(view, markdownText);
      return true;
    },
    drop(event, view) {
      const markdownText = imageMarkdownFromFiles(Array.from(event.dataTransfer?.files ?? []));
      if (!markdownText) {
        return false;
      }

      event.preventDefault();
      replaceSelection(view, markdownText);
      return true;
    },
  });
}

function mount(root, dotNet, options = {}) {
  dispose(root);

  const host = getHost(root);
  if (!host) {
    return;
  }

  const view = new EditorView({
    parent: host,
    state: EditorState.create({
      doc: options.value ?? "",
      extensions: [
        minimalSetup,
        markdown(),
        keymap.of([indentWithTab]),
        EditorView.lineWrapping,
        bocchiTheme,
        editorPlaceholder(options.placeholder ?? ""),
        buildPasteAndDropHandlers(),
        EditorView.updateListener.of((update) => {
          if (!update.docChanged) {
            return;
          }

          dotNet.invokeMethodAsync("HandleEditorInputAsync", update.state.doc.toString());
        }),
      ],
    }),
  });

  editorByRoot.set(root, { view, dotNet, snippets: options.snippets ?? {} });
  setRootViewMode(root, options.defaultViewMode);
}

function setValue(root, value) {
  const view = getEditor(root);
  if (!view) {
    return;
  }

  const current = view.state.doc.toString();
  if (current === value) {
    return;
  }

  view.dispatch({
    changes: { from: 0, to: current.length, insert: value ?? "" },
  });
}

function insert(root, action) {
  const current = editorByRoot.get(root);
  if (!current?.view) {
    return;
  }

  const selection = current.view.state.selection.main;
  const selected = current.view.state.sliceDoc(selection.from, selection.to);
  replaceSelection(current.view, createInsertText(action, selected, current.snippets));
}

function setViewMode(root, mode) {
  setRootViewMode(root, mode);
  getEditor(root)?.requestMeasure();
}

function dispose(root) {
  const current = editorByRoot.get(root);
  if (!current) {
    return;
  }

  current.view.destroy();
  editorByRoot.delete(root);
}

// Blazor 只依赖这个全局桥接对象；内部编辑器实现可以继续演进。
window.bocchiMarkdownEditor = {
  mount,
  setValue,
  insert,
  setViewMode,
  dispose,
};
