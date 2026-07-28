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
  summary.className = "ccs-prop-section-summary";
  const chevron = document.createElement("span");
  chevron.className = "ccs-prop-section-chevron";
  chevron.setAttribute("aria-hidden", "true");
  const label = document.createElement("span");
  label.className = "ccs-prop-section-title";
  label.textContent = title;
  summary.appendChild(chevron);
  summary.appendChild(label);
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

/** Primary content — open by default (Widget-Tab). */
export function contentSection(id: string, title = "Inhalt") {
  return propSection(`${id}-content`, title, false);
}

/** Variant / size presets — open by default (Widget-Tab). */
export function lookSection(id: string, title = "Look") {
  return propSection(`${id}-look`, title, false);
}

/** Colors, fonts, spacing — collapsed by default (extended). */
export function styleSection(id: string, title = "Stil") {
  return propSection(`${id}-style`, title, true);
}

/** Feature toggles / extras — collapsed by default (extended). */
export function advancedSection(id: string, title = "Erweitert") {
  return propSection(`${id}-advanced`, title, true);
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

  // Match boolProp row: Label | Checkbox
  const header = document.createElement("div");
  header.className = "ccs-prop-row ccs-feature-header";

  const label = document.createElement("span");
  label.className = "ccs-prop-row-label ccs-feature-title";
  label.textContent = title;

  const enabled = !enabledKey || (item?.props?.[enabledKey] !== false);
  const checkbox = document.createElement("input");
  checkbox.type = "checkbox";
  checkbox.className = "ccs-check";
  checkbox.checked = enabled;

  header.appendChild(label);
  header.appendChild(checkbox);
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
