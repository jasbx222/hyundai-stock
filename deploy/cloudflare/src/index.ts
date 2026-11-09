import { Container, getContainer } from "@cloudflare/containers";

export interface Env {
  BACKEND: DurableObjectNamespace<HyundaiBackend>;
  SLEEP_AFTER?: string;
  ASPNETCORE_ENVIRONMENT?: string;
  DB_CONNECTION_STRING?: string;
  JWT_KEY?: string;
  SEED_ADMIN_NAME?: string;
  SEED_ADMIN_PHONE?: string;
  SEED_ADMIN_PASSWORD?: string;
  FCM_SERVER_KEY?: string;
}

const PORT = 8080;

/** The ASP.NET Core app (dashboard + mobile API) running in a Cloudflare Container. */
export class HyundaiBackend extends Container<Env> {
  defaultPort = PORT;
  sleepAfter = "2h";
  // Needed to reach the external SQL Server database and FCM.
  enableInternet = true;

  constructor(ctx: DurableObjectState<{}>, env: Env) {
    super(ctx, env);
    if (env.SLEEP_AFTER) this.sleepAfter = env.SLEEP_AFTER;
    const vars: Record<string, string> = {
      ASPNETCORE_ENVIRONMENT: env.ASPNETCORE_ENVIRONMENT || "Production",
      Database__Provider: "SqlServer",
    };
    const put = (key: string, value: string | undefined) => {
      if (value) vars[key] = value;
    };
    put("ConnectionStrings__Default", env.DB_CONNECTION_STRING);
    put("Jwt__Key", env.JWT_KEY);
    put("Seed__MainAdminName", env.SEED_ADMIN_NAME);
    put("Seed__MainAdminPhone", env.SEED_ADMIN_PHONE);
    put("Seed__MainAdminPassword", env.SEED_ADMIN_PASSWORD);
    put("Notifications__FcmServerKey", env.FCM_SERVER_KEY);
    this.envVars = vars;
  }

  override async fetch(request: Request): Promise<Response> {
    const state = await this.getState();
    if (state.status !== "healthy") {
      try {
        // .NET start-up plus EF migrations (and a waking database) can exceed the 20 s default.
        await this.startAndWaitForPorts(PORT, {
          instanceGetTimeoutMS: 60_000,
          portReadyTimeoutMS: 180_000,
          abort: request.signal,
        });
      } catch (err) {
        console.error("container start failed", err);
        return startingResponse(request);
      }
    }
    return this.containerFetch(request, PORT);
  }

  override onError(error: unknown) {
    console.error("container error", error);
    throw error;
  }
}

function startingResponse(request: Request): Response {
  const isApi = new URL(request.url).pathname.toLowerCase().startsWith("/api");
  if (isApi) {
    return Response.json(
      { success: false, message: "الخادم قيد التشغيل، حاول بعد لحظات", data: null, errors: null },
      { status: 503, headers: { "Retry-After": "15" } },
    );
  }
  return new Response(
    '<!DOCTYPE html><html lang="ar" dir="rtl"><meta charset="utf-8"><meta http-equiv="refresh" content="10"><body style="font-family:sans-serif;text-align:center;padding:3rem"><h2>النظام قيد التشغيل…</h2><p>ستُحدَّث الصفحة تلقائياً خلال ثوانٍ.</p></body></html>',
    { status: 503, headers: { "Content-Type": "text/html; charset=utf-8", "Retry-After": "10" } },
  );
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const headers = new Headers(request.headers);
    // The app trusts exactly one X-Forwarded-Proto from its loopback forwarder;
    // overwrite anything the client sent so it cannot be spoofed.
    headers.set("X-Forwarded-Proto", url.protocol === "https:" ? "https" : "http");
    headers.delete("X-Forwarded-Host");
    const forwarded = new Request(request, { headers });
    // Single named instance: the app keeps rate limits and data-protection keys in memory.
    return getContainer(env.BACKEND, "primary").fetch(forwarded);
  },
} satisfies ExportedHandler<Env>;
