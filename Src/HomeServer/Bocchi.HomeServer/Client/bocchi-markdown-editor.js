import { minimalSetup } from "codemirror";
import { markdown } from "@codemirror/lang-markdown";
import { EditorState } from "@codemirror/state";
import { EditorView, keymap, placeholder as editorPlaceholder } from "@codemirror/view";
import { indentWithTab } from "@codemirror/commands";

const editorByRoot = new WeakMap();
const imageFileAccept = ".jpg,.jpeg,.png,.gif,.webp,.avif,image/jpeg,image/png,image/gif,image/webp,image/avif";
const imageFileExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif"];

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

  editorByRoot.set(root, { view, dotNet, snippets: options.snippets ?? {}, imagePicker: null });
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
  pickImages,
  dispose,
};
