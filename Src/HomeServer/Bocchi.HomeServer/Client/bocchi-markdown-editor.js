import { minimalSetup } from "codemirror";
import { markdown } from "@codemirror/lang-markdown";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { EditorState } from "@codemirror/state";
import { EditorView, keymap, placeholder as editorPlaceholder } from "@codemirror/view";
import { indentWithTab } from "@codemirror/commands";
import { tags as t } from "@lezer/highlight";

const editorByRoot = new WeakMap();
const imageFileAccept = ".jpg,.jpeg,.png,.gif,.webp,.avif,image/jpeg,image/png,image/gif,image/webp,image/avif";
const imageFileExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif"];

/** Bocchi Markdown 语法高亮：使用 Dashboard token，避免 defaultHighlightStyle 在 dark 下对比度不足。 */
const bocchiMarkdownHighlight = HighlightStyle.define([
  { tag: t.heading, color: "var(--bocchi-editor-syntax-heading)", fontWeight: "650" },
  { tag: [t.heading1, t.heading2, t.heading3, t.heading4, t.heading5, t.heading6], color: "var(--bocchi-editor-syntax-heading)", fontWeight: "700" },
  { tag: t.emphasis, fontStyle: "italic", color: "var(--bocchi-editor-syntax-text)" },
  { tag: t.strong, fontWeight: "700", color: "var(--bocchi-editor-syntax-text)" },
  { tag: t.strikethrough, textDecoration: "line-through", color: "var(--bocchi-editor-syntax-muted)" },
  { tag: t.link, color: "var(--bocchi-editor-syntax-link)", textDecoration: "underline", textUnderlineOffset: "2px" },
  { tag: t.url, color: "var(--bocchi-editor-syntax-url)" },
  { tag: t.meta, color: "var(--bocchi-editor-syntax-marker)" },
  { tag: t.monospace, color: "var(--bocchi-editor-syntax-code)", backgroundColor: "var(--bocchi-editor-syntax-code-bg)", borderRadius: "3px" },
  { tag: t.comment, color: "var(--bocchi-editor-syntax-muted)", fontStyle: "italic" },
  { tag: t.quote, color: "var(--bocchi-editor-syntax-muted)", fontStyle: "italic" },
  { tag: [t.processingInstruction, t.labelName, t.atom], color: "var(--bocchi-editor-syntax-fence)" },
  { tag: t.contentSeparator, color: "var(--bocchi-editor-syntax-marker)" },
  { tag: t.keyword, color: "var(--bocchi-editor-syntax-marker)" },
  { tag: t.string, color: "var(--bocchi-editor-syntax-text)" },
  { tag: t.name, color: "var(--bocchi-editor-syntax-text)" },
  { tag: t.propertyName, color: "var(--bocchi-editor-syntax-code)" },
]);

const bocchiTheme = EditorView.theme({
  "&": {
    minHeight: "100%",
    background: "transparent",
    color: "var(--bocchi-editor-syntax-text)",
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
    backgroundColor: "var(--bocchi-editor-syntax-active-line)",
  },
  ".cm-selectionBackground": {
    backgroundColor: "var(--bocchi-editor-syntax-selection) !important",
  },
  ".cm-placeholder": {
    color: "var(--bocchi-text-subtle)",
  },
  ".cm-specialChar": {
    color: "var(--bocchi-editor-syntax-marker)",
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
  ".tok-meta": { color: "var(--bocchi-editor-syntax-marker) !important" },
  ".tok-link": { color: "var(--bocchi-editor-syntax-link) !important" },
  ".tok-url": { color: "var(--bocchi-editor-syntax-url) !important" },
  ".tok-monospace": {
    color: "var(--bocchi-editor-syntax-code) !important",
    backgroundColor: "var(--bocchi-editor-syntax-code-bg) !important",
  },
  ".tok-processingInstruction": { color: "var(--bocchi-editor-syntax-fence) !important" },
  ".tok-labelName": { color: "var(--bocchi-editor-syntax-fence) !important" },
  ".tok-heading": { color: "var(--bocchi-editor-syntax-heading) !important" },
  ".tok-strong": { color: "var(--bocchi-editor-syntax-text) !important" },
  ".tok-emphasis": { color: "var(--bocchi-editor-syntax-text) !important" },
  ".cm-meta": { color: "var(--bocchi-editor-syntax-marker) !important" },
  ".cm-link": { color: "var(--bocchi-editor-syntax-link) !important" },
  ".cm-url": { color: "var(--bocchi-editor-syntax-url) !important" },
  ".cm-monospace": {
    color: "var(--bocchi-editor-syntax-code) !important",
    backgroundColor: "var(--bocchi-editor-syntax-code-bg) !important",
  },
  ".cm-comment": { color: "var(--bocchi-editor-syntax-muted) !important" },
  ".cm-quote": { color: "var(--bocchi-editor-syntax-muted) !important" },
  ".cm-header": { color: "var(--bocchi-editor-syntax-heading) !important", fontWeight: "650" },
  ".cm-header-1": { fontWeight: "700" },
  ".cm-header-2": { fontWeight: "680" },
  ".cm-strong": { color: "var(--bocchi-editor-syntax-text) !important", fontWeight: "700" },
  ".cm-emphasis": { color: "var(--bocchi-editor-syntax-text) !important", fontStyle: "italic" },
  ".cm-strikethrough": { color: "var(--bocchi-editor-syntax-muted) !important" },
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

/** 窄屏没有 split 预览栏，避免 localStorage 里的 split 撑破移动端布局。 */
function resolveViewModeForViewport(mode) {
  const normalized = normalizeViewMode(mode);
  if (normalized === "split" && window.matchMedia("(max-width: 720px)").matches) {
    return "write";
  }

  return normalized;
}

const viewModeStorageKey = "bocchi-markdown-view-mode";

function readStoredViewMode(fallback) {
  try {
    const stored = localStorage.getItem(viewModeStorageKey);
    return resolveViewModeForViewport(stored ? normalizeViewMode(stored) : normalizeViewMode(fallback));
  } catch {
    return resolveViewModeForViewport(normalizeViewMode(fallback));
  }
}

function persistViewMode(mode) {
  try {
    localStorage.setItem(viewModeStorageKey, normalizeViewMode(mode));
  } catch {
    // localStorage 不可用时忽略。
  }
}

function setRootViewMode(root, mode) {
  if (root) {
    root.dataset.view = normalizeViewMode(mode);
  }
}

function headingPrefix(level) {
  const safe = Math.min(6, Math.max(1, level));
  return "#".repeat(safe) + " ";
}

function parseHeadingLevel(action) {
  const match = /^heading-([1-6])$/.exec(action);
  if (!match) {
    return 2;
  }

  return Number(match[1]);
}

function createInsertText(action, selected, snippets = {}) {
  const text = selected || "";
  if (action === "heading" || action.startsWith("heading-")) {
    const level = parseHeadingLevel(action);
    return prefixLines(text || snippets.heading || "Heading", headingPrefix(level));
  }

  switch (action) {
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

function replaceSelection(view, text, position = null) {
  const selection = position === null
    ? view.state.selection.main
    : { from: position, to: position };
  view.dispatch({
    changes: { from: selection.from, to: selection.to, insert: text },
    selection: { anchor: selection.from + text.length },
    scrollIntoView: true,
  });
  view.focus();
}

function isImageFile(file) {
  if (file?.type?.startsWith("image/") === true) {
    return true;
  }

  const name = file?.name?.toLowerCase() ?? "";
  return imageFileExtensions.some((extension) => name.endsWith(extension));
}

function imageFilesFrom(fileList) {
  return Array.from(fileList ?? []).filter(isImageFile);
}

function fallbackImageName(file, index) {
  if (file.name) {
    return file.name;
  }

  const extension = {
    "image/jpeg": ".jpg",
    "image/png": ".png",
    "image/gif": ".gif",
    "image/webp": ".webp",
    "image/avif": ".avif",
    "image/svg+xml": ".svg",
  }[file.type] ?? ".png";
  return `uploaded-image-${index + 1}${extension}`;
}

async function uploadImageFiles(root, view, files, insertPosition = null) {
  const current = editorByRoot.get(root);
  if (!current?.dotNet) {
    return;
  }

  const markdown = [];
  for (let index = 0; index < files.length; index += 1) {
    const file = files[index];
    try {
      const result = await current.dotNet.invokeMethodAsync(
        "HandleImageUploadAsync",
        fallbackImageName(file, index),
        file.type ?? "",
        DotNet.createJSStreamReference(file));
      const inserted = result?.markdown ?? result?.Markdown;
      if (inserted) {
        markdown.push(inserted);
      }
    } catch (error) {
      await current.dotNet.invokeMethodAsync(
        "HandleImageUploadFailureAsync",
        error?.message ?? "");
    }
  }

  if (markdown.length > 0) {
    replaceSelection(view, markdown.join("\n"), insertPosition);
  }
}

function dropInsertPosition(event, view) {
  return view.posAtCoords({ x: event.clientX, y: event.clientY }) ?? null;
}

function buildPasteAndDropHandlers(root) {
  return EditorView.domEventHandlers({
    paste(event, view) {
      const files = imageFilesFrom(event.clipboardData?.files);
      if (files.length === 0) {
        return false;
      }

      event.preventDefault();
      void uploadImageFiles(root, view, files);
      return true;
    },
    dragover(event) {
      if (imageFilesFrom(event.dataTransfer?.items).length === 0 &&
          imageFilesFrom(event.dataTransfer?.files).length === 0) {
        return false;
      }

      event.preventDefault();
      return true;
    },
    drop(event, view) {
      const files = imageFilesFrom(event.dataTransfer?.files);
      if (files.length === 0) {
        return false;
      }

      const position = dropInsertPosition(event, view);
      event.preventDefault();
      void uploadImageFiles(root, view, files, position);
      return true;
    },
  });
}

function createImagePicker(root) {
  const input = document.createElement("input");
  input.type = "file";
  input.accept = imageFileAccept;
  input.multiple = true;
  input.hidden = true;
  input.setAttribute("aria-hidden", "true");
  input.addEventListener("change", () => {
    const current = editorByRoot.get(root);
    const files = imageFilesFrom(input.files);
    input.value = "";
    if (!current?.view || files.length === 0) {
      return;
    }

    void uploadImageFiles(root, current.view, files);
  });
  root.append(input);
  return input;
}

function resolveViewMode(fallback) {
  return readStoredViewMode(fallback ?? "write");
}

function getViewMode(root) {
  return normalizeViewMode(root?.dataset?.view ?? "write");
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
        syntaxHighlighting(bocchiMarkdownHighlight, { fallback: false }),
        keymap.of([indentWithTab]),
        EditorView.lineWrapping,
        bocchiTheme,
        editorPlaceholder(options.placeholder ?? ""),
        buildPasteAndDropHandlers(root),
        EditorView.updateListener.of((update) => {
          if (!update.docChanged) {
            return;
          }

          dotNet.invokeMethodAsync("HandleEditorInputAsync", update.state.doc.toString());
        }),
      ],
    }),
  });

  editorByRoot.set(root, {
    view,
    dotNet,
    snippets: options.snippets ?? {},
    imagePicker: null,
    unbindHeadingMenuDismiss: bindHeadingMenuDismiss(root),
  });
  setRootViewMode(root, readStoredViewMode(options.defaultViewMode ?? "write"));
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
  if (action === "heading" || action.startsWith("heading-")) {
    closeHeadingMenu(root);
  }
}

function closeHeadingMenu(root) {
  root?.querySelector(".bocchi-markdown-toolbar__heading-menu")?.removeAttribute("open");
}

/** 标题菜单展开时，点击外部区域自动收起。 */
function bindHeadingMenuDismiss(root) {
  const menu = root.querySelector(".bocchi-markdown-toolbar__heading-menu");
  if (!menu) {
    return () => {};
  }

  let dismissHandler = null;

  const onToggle = () => {
    if (dismissHandler) {
      document.removeEventListener("pointerdown", dismissHandler, true);
      dismissHandler = null;
    }

    if (!menu.open) {
      return;
    }

    dismissHandler = (event) => {
      if (menu.contains(event.target)) {
        return;
      }

      closeHeadingMenu(root);
    };

    document.addEventListener("pointerdown", dismissHandler, true);
  };

  menu.addEventListener("toggle", onToggle);

  return () => {
    if (dismissHandler) {
      document.removeEventListener("pointerdown", dismissHandler, true);
    }

    menu.removeEventListener("toggle", onToggle);
  };
}

function setViewMode(root, mode) {
  const normalized = resolveViewModeForViewport(mode);
  setRootViewMode(root, normalized);
  persistViewMode(normalized);
  getEditor(root)?.requestMeasure();
}

function pickImages(root) {
  const current = editorByRoot.get(root);
  if (!current?.view) {
    return;
  }

  current.imagePicker ??= createImagePicker(root);
  current.imagePicker.click();
}

function dispose(root) {
  const current = editorByRoot.get(root);
  if (!current) {
    return;
  }

  current.unbindHeadingMenuDismiss?.();
  current.imagePicker?.remove();
  current.view.destroy();
  editorByRoot.delete(root);
}

// Blazor 只依赖这个全局桥接对象；内部编辑器实现可以继续演进。
window.bocchiMarkdownEditor = {
  mount,
  setValue,
  insert,
  setViewMode,
  resolveViewMode,
  getViewMode,
  closeHeadingMenu,
  pickImages,
  dispose,
};
