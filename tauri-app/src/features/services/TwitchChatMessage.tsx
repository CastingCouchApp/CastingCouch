function list(raw: string | undefined): Record<string, unknown>[] {
    try {
        const value: unknown = JSON.parse(raw || "[]");
        return Array.isArray(value)
            ? value.filter(
                  (item): item is Record<string, unknown> =>
                      item !== null && typeof item === "object",
              )
            : [];
    } catch {
        return [];
    }
}
function imageUrl(value: unknown): string | undefined {
    if (typeof value !== "string") return;
    try {
        const url = new URL(value);
        return ["https:", "http:"].includes(url.protocol)
            ? url.href
            : undefined;
    } catch {
        return;
    }
}
function text(value: unknown): string {
    return typeof value === "string" ? value : "";
}
export function TwitchChatMessage({ data }: { data: Record<string, string> }) {
    const parts = list(data.parts);
    return (
        <span className="flex flex-1 min-w-0 items-baseline gap-1">
            {list(data.badges).map(
                (badge, index) =>
                    imageUrl(badge.url) && (
                        <img
                            key={index}
                            className="inline-block h-4 w-4 shrink-0"
                            src={imageUrl(badge.url)}
                            alt={text(badge.title)}
                            title={text(badge.title)}
                            loading="lazy"
                        />
                    ),
            )}
            <strong
                style={{
                    color: /^#[0-9a-f]{6}$/i.test(data.color || "")
                        ? data.color
                        : undefined,
                }}
            >
                {data.userName || data.userLogin}
            </strong>
            <span className="flex-1 whitespace-pre-wrap break-words">
                {parts.length
                    ? parts.map((part, index) =>
                          part.type === "emote" && imageUrl(part.url) ? (
                              <img
                                  key={index}
                                  className="inline-block h-6 w-auto align-middle"
                                  src={imageUrl(part.url)}
                                  alt={text(part.text)}
                                  title={text(part.text)}
                                  loading="lazy"
                              />
                          ) : (
                              <span key={index}>{text(part.text)}</span>
                          ),
                      )
                    : data.text}
            </span>
        </span>
    );
}
