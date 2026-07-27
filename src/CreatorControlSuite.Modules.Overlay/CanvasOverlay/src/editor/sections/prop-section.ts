const STORAGE_PREFIX = "ccs-prop-section:";

export function propSection(
  id: string,
  title: string,
  collapsedByDefault = false
): { root: HTMLDetailsElement; body: HTMLDivElement } {
  const root = document.createElement("details");
  root.className = "ccs-prop-section";
  root.dataset.sectionId = id;

  let collapsed = collapsedByDefault;
  try {
    const stored = sessionStorage.getItem(STORAGE_PREFIX + id);
    if (stored === "1") collapsed = true;
    if (stored === "0") collapsed = false;
  } catch {
    /* ignore */
  }
  root.open = !collapsed;

  const summary = document.createElement("summary");
  summary.textContent = title;
  root.appendChild(summary);

  const body = document.createElement("div");
  body.className = "ccs-prop-section-body";
  root.appendChild(body);

  root.addEventListener("toggle", () => {
    try {
      sessionStorage.setItem(STORAGE_PREFIX + id, root.open ? "0" : "1");
    } catch {
      /* ignore */
    }
  });

  return { root, body };
}

export function featureSection(options: {
  id: string;
  title: string;
  enabledKey?: string;
  item?: { props?: Record<string, unknown> };
  commit?: (apply: (live: { props: Record<string, unknown> }) => void) => void;
  children?: (body: HTMLElement) => void;
}): HTMLElement {
  const { id, title, enabledKey, item, commit, children } = options;
  const root = document.createElement("div");
  root.className = "ccs-feature-section";
  root.dataset.featureId = id;

  const header = document.createElement("label");
  header.className = "ccs-feature-header";
  const enabled = !enabledKey || (item?.props?.[enabledKey] !== false);
  const checkbox = document.createElement("input");
  checkbox.type = "checkbox";
  checkbox.checked = enabled;
  const label = document.createElement("span");
  label.textContent = title;
  header.appendChild(checkbox);
  header.appendChild(label);
  root.appendChild(header);

  const body = document.createElement("div");
  body.className = "ccs-feature-body";
  if (!checkbox.checked) {
    body.classList.add("ccs-feature-disabled");
  }
  root.appendChild(body);

  if (enabledKey && commit) {
    checkbox.addEventListener("change", () => {
      commit((live) => {
        live.props = live.props || {};
        live.props[enabledKey] = checkbox.checked;
      });
      body.classList.toggle("ccs-feature-disabled", !checkbox.checked);
    });
  } else if (!enabledKey) {
    checkbox.hidden = true;
  }

  if (children) children(body);
  return root;
}
