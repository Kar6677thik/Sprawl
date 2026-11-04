import { describe, expect, it } from "vitest";
import { ApiError, SprawlClient } from "./client";

function recorder(status: number, body: unknown) {
  const calls: { url: string; init: RequestInit }[] = [];
  const fetcher = async (url: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(url), init: init ?? {} });
    return new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
  };
  return { calls, client: new SprawlClient(fetcher as typeof fetch) };
}

describe("SprawlClient", () => {
  it("sends the anti-CSRF header and If-Match on writes, but not on reads", async () => {
    const { calls, client } = recorder(200, { version: 8 });
    await client.batch("cv_00000001", { intents: [{ type: "deleteCards", ids: ["cd_00000001"] }] }, 7);
    await client.canvas("cv_00000001");
    const write = calls[0]!.init.headers as Record<string, string>;
    expect(write["X-Sprawl-Request"]).toBe("1");
    expect(write["If-Match"]).toBe('"7"');
    expect(JSON.parse(calls[0]!.init.body as string).requestId).toMatch(/^[0-9a-f-]{36}$/);
    expect((calls[1]!.init.headers as Record<string, string>)["X-Sprawl-Request"]).toBeUndefined();
  });

  it("turns problem+json into an ApiError with the stable code and current version", async () => {
    const { client } = recorder(412, { code: "stale_version", detail: "The canvas changed.", currentVersion: 9 });
    const error = await client.canvas("cv_00000001").catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 412, code: "stale_version", currentVersion: 9, message: "The canvas changed." });
  });
});
