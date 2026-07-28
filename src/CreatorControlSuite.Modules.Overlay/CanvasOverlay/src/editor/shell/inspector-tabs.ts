export const PROPS_TAB_STORAGE_KEY = "ccs-props-tab";

export function activateInspectorTab(
  tabs: HTMLButtonElement[],
  panes: HTMLElement[],
  tabId: string
): string {
  const next = tabs.some((t) => t.dataset.tab === tabId) ? tabId : "layout";
  for (const tab of tabs) {
    const selected = tab.dataset.tab === next;
    tab.setAttribute("aria-selected", selected ? "true" : "false");
  }
  for (const pane of panes) {
    pane.hidden = pane.dataset.pane !== next;
  }
  return next;
}

export function wireInspectorTabs(storage: Storage = sessionStorage): void {
  const tabsRoot = document.getElementById("propsTabs");
  const form = document.getElementById("propsForm");
  if (!tabsRoot || !form) return;

  const tabs = Array.from(tabsRoot.querySelectorAll<HTMLButtonElement>(".ccs-props-tab"));
  const panes = Array.from(form.querySelectorAll<HTMLElement>(".ccs-props-pane"));

  function activate(tabId: string): void {
    const next = activateInspectorTab(tabs, panes, tabId);
    try {
      storage.setItem(PROPS_TAB_STORAGE_KEY, next);
    } catch {
      /* ignore */
    }
  }

  let stored = "layout";
  try {
    stored = storage.getItem(PROPS_TAB_STORAGE_KEY) || "layout";
  } catch {
    /* ignore */
  }
  activate(stored);

  tabsRoot.addEventListener("click", (e) => {
    const btn = (e.target as HTMLElement).closest<HTMLButtonElement>(".ccs-props-tab");
    if (!btn || !btn.dataset.tab) return;
    activate(btn.dataset.tab);
  });
}
