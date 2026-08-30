namespace SurvivalBackend.Admin;

public static class AdminPanel
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Survival Backend Admin</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #111318;
      --panel: #181c23;
      --panel-2: #202631;
      --border: #303744;
      --text: #eef2f7;
      --muted: #9aa6b7;
      --accent: #54c6a1;
      --danger: #ef6b73;
      --warn: #f4bf5f;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: var(--bg);
      color: var(--text);
      font: 14px/1.45 system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
    }
    header, main {
      width: min(1180px, calc(100vw - 32px));
      margin: 0 auto;
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 22px 0 14px;
      border-bottom: 1px solid var(--border);
    }
    h1 {
      margin: 0;
      font-size: 22px;
      font-weight: 700;
    }
    .toolbar {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
    }
    input, button {
      height: 36px;
      border-radius: 6px;
      border: 1px solid var(--border);
      background: var(--panel-2);
      color: var(--text);
      font: inherit;
    }
    input {
      width: min(320px, 70vw);
      padding: 0 10px;
    }
    button {
      padding: 0 12px;
      cursor: pointer;
    }
    button.primary { border-color: #3f8f78; background: #1d4f43; }
    button.danger { border-color: #8a3b42; background: #4b2027; }
    button:disabled { opacity: .55; cursor: wait; }
    main {
      display: grid;
      gap: 18px;
      padding: 18px 0 36px;
    }
    .metrics {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
    }
    .metric, section {
      border: 1px solid var(--border);
      border-radius: 8px;
      background: var(--panel);
    }
    .metric {
      padding: 12px;
      min-height: 78px;
    }
    .metric span {
      display: block;
      color: var(--muted);
      font-size: 12px;
      margin-bottom: 7px;
    }
    .metric strong {
      display: block;
      font-size: 20px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    section {
      overflow: hidden;
    }
    section header {
      width: auto;
      margin: 0;
      padding: 12px;
      border-bottom: 1px solid var(--border);
    }
    section h2 {
      margin: 0;
      font-size: 15px;
    }
    .status {
      padding: 10px 12px;
      color: var(--muted);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      table-layout: fixed;
    }
    th, td {
      padding: 10px 12px;
      border-bottom: 1px solid var(--border);
      text-align: left;
      vertical-align: top;
      overflow-wrap: anywhere;
    }
    th {
      color: var(--muted);
      font-size: 12px;
      font-weight: 600;
      background: #151922;
    }
    tr:last-child td { border-bottom: 0; }
    .pill {
      display: inline-flex;
      align-items: center;
      height: 22px;
      padding: 0 8px;
      border-radius: 999px;
      background: #28303d;
      color: var(--muted);
      font-size: 12px;
      font-weight: 600;
    }
    .pill.ok { background: #163b32; color: var(--accent); }
    .pill.warn { background: #45361d; color: var(--warn); }
    .pill.error { background: #452229; color: var(--danger); }
    .small { color: var(--muted); font-size: 12px; }
    @media (max-width: 820px) {
      header { align-items: flex-start; flex-direction: column; }
      .metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      table { min-width: 760px; }
      .table-wrap { overflow-x: auto; }
    }
    @media (max-width: 520px) {
      .metrics { grid-template-columns: 1fr; }
      .toolbar { width: 100%; }
      input { width: 100%; }
    }
  </style>
</head>
<body>
  <header>
    <h1>Survival Backend Admin</h1>
    <div class="toolbar">
      <input id="apiKey" type="password" placeholder="Admin API key" autocomplete="off">
      <button id="saveKey">Save</button>
      <button id="refresh" class="primary">Refresh</button>
      <button id="releaseMissing">Release Missing</button>
      <button id="runWipe" class="danger">Run Wipe</button>
    </div>
  </header>
  <main>
    <div class="metrics">
      <div class="metric"><span>Game client</span><strong id="clientVersion">-</strong></div>
      <div class="metric"><span>Registry</span><strong id="registry">-</strong></div>
      <div class="metric"><span>Edgegap</span><strong id="edgegap">-</strong></div>
      <div class="metric"><span>Wipe</span><strong id="wipe">-</strong></div>
    </div>
    <section>
      <header><h2>Servers</h2></header>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Unique ID</th>
              <th>Request ID</th>
              <th>Status</th>
              <th>Players</th>
              <th>Edgegap IP</th>
            </tr>
          </thead>
          <tbody id="servers"></tbody>
        </table>
      </div>
    </section>
    <section>
      <header><h2>Activity</h2></header>
      <div id="activity" class="status">Ready.</div>
    </section>
  </main>
  <script>
    const keyInput = document.querySelector("#apiKey");
    const activity = document.querySelector("#activity");
    const serversBody = document.querySelector("#servers");
    const buttons = [...document.querySelectorAll("button")];

    keyInput.value = localStorage.getItem("survival-admin-key") || "";

    document.querySelector("#saveKey").addEventListener("click", () => {
      localStorage.setItem("survival-admin-key", keyInput.value);
      setActivity("Saved.");
    });
    document.querySelector("#refresh").addEventListener("click", refresh);
    document.querySelector("#releaseMissing").addEventListener("click", () => postAction("/admin/api/servers/release-missing"));
    document.querySelector("#runWipe").addEventListener("click", () => postAction("/admin/api/wipe/run"));

    async function request(path, options = {}) {
      const headers = new Headers(options.headers || {});
      if (keyInput.value) headers.set("X-Admin-Api-Key", keyInput.value);
      const response = await fetch(path, { ...options, headers });
      const text = await response.text();
      const data = text ? safeJson(text) : null;
      if (!response.ok) throw new Error(data?.message || data?.Message || text || response.statusText);
      return data;
    }

    function safeJson(text) {
      try { return JSON.parse(text); } catch { return { message: text }; }
    }

    async function refresh() {
      await withBusy(async () => {
        setActivity("Loading overview...");
        const data = await request("/admin/api/overview");
        renderOverview(data);
        setActivity(`Updated ${new Date(data.generatedAtUtc).toLocaleString()}.`);
      });
    }

    async function postAction(path) {
      await withBusy(async () => {
        setActivity("Sending command...");
        const data = await request(path, { method: "POST" });
        setActivity(data.message || data.Message || "Done.");
        await refresh();
      });
    }

    async function withBusy(work) {
      buttons.forEach(button => button.disabled = true);
      try {
        await work();
      } catch (error) {
        setActivity(error.message || String(error), "error");
      } finally {
        buttons.forEach(button => button.disabled = false);
      }
    }

    function renderOverview(data) {
      document.querySelector("#clientVersion").textContent = data.gameClientVersion || "-";
      document.querySelector("#registry").textContent = `${data.registry.serversCount} / ${data.registry.storageMode}`;
      document.querySelector("#edgegap").textContent = data.edgegap.status === "Ok"
        ? `${data.edgegap.readyDeploymentsCount}/${data.edgegap.deploymentsCount} ready`
        : "Error";
      document.querySelector("#wipe").textContent = data.wipe.status;

      serversBody.innerHTML = "";
      for (const server of data.servers) {
        const runtime = server.runtime;
        const players = runtime ? `${runtime.currentPlayersCount}/${runtime.maxPlayersCount}` : "-";
        const readyClass = server.ready ? "ok" : "warn";
        const readyText = server.ready ? "Ready" : "Waiting";
        const edgegapIp = server.edgegap?.publicIp || "-";
        const row = document.createElement("tr");
        row.innerHTML = `
          <td>${escapeHtml(server.serverName)}</td>
          <td><span class="small">${escapeHtml(server.uniqueId)}</span></td>
          <td><span class="small">${escapeHtml(server.requestId)}</span></td>
          <td><span class="pill ${readyClass}">${readyText}</span></td>
          <td>${escapeHtml(players)}</td>
          <td>${escapeHtml(edgegapIp)}</td>`;
        serversBody.appendChild(row);
      }

      if (data.servers.length === 0) {
        const row = document.createElement("tr");
        row.innerHTML = `<td colspan="6" class="small">No servers.</td>`;
        serversBody.appendChild(row);
      }
    }

    function setActivity(message, type = "") {
      activity.textContent = message;
      activity.className = `status ${type}`;
    }

    function escapeHtml(value) {
      return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");
    }

    refresh();
  </script>
</body>
</html>
""";
}
