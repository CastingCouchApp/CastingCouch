import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { describe, expect, it } from "vitest";
import { routeTree } from "../../routeTree.gen";

describe("Overlay route", () => {
  it("renders canvas table heading", async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: ["/overlay"] }),
    });
    render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    );
    expect(await screen.findByRole("heading", { name: "Overlay" })).toBeInTheDocument();
    expect(await screen.findByText("Canvas anlegen")).toBeInTheDocument();
    expect(await screen.findByText("Canvas")).toBeInTheDocument();
  });
});
