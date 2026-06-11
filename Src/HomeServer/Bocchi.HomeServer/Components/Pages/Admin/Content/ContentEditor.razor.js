/** ContentEditor 顶栏标题 mirror：标题滚出视口后在 commandbar 显示截断标题。 */
let disposeTitleMirror = null;

/**
 * @param {HTMLElement} editorRoot
 * @param {import("@microsoft/dotnet-js-interop").DotNetObject} dotNetRef
 * @param {HTMLElement} anchor
 */
export function mountTitleMirror(editorRoot, dotNetRef, anchor) {
  releaseTitleMirror();

  if (!editorRoot || !dotNetRef || !anchor) {
    return;
  }

  const readStickyOffset = () => {
    const topbar = document.querySelector(".bocchi-topbar");
    const commandbar = editorRoot.querySelector(".bocchi-editor-commandbar");
    return (topbar?.offsetHeight ?? 0) + (commandbar?.offsetHeight ?? 0) + 8;
  };

  let observer = null;

  const notifyVisibility = (entries) => {
    const visible = entries.some((entry) => entry.isIntersecting);
    dotNetRef.invokeMethodAsync("SetTitleFieldVisible", visible);
  };

  const rebuildObserver = () => {
    observer?.disconnect();
    observer = new IntersectionObserver(notifyVisibility, {
      root: null,
      rootMargin: `${-readStickyOffset()}px 0px 0px 0px`,
      threshold: 0,
    });
    observer.observe(anchor);
  };

  rebuildObserver();
  window.addEventListener("resize", rebuildObserver);

  disposeTitleMirror = () => {
    observer?.disconnect();
    window.removeEventListener("resize", rebuildObserver);
    disposeTitleMirror = null;
  };
}

export function releaseTitleMirror() {
  disposeTitleMirror?.();
}
