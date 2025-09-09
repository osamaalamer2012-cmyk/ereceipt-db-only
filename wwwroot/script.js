/* wwwroot/script.js — robust client with safe JSON handling and clear errors */

/* ---------- tiny helpers ---------- */
const el = (id) => document.getElementById(id);

function safeParseJson(text) {
  if (!text) return null;
  try { return JSON.parse(text); } catch { return null; }
}

async function fetchSafe(url, options = {}) {
  try {
    const res = await fetch(url, options);
    const text = await res.text();                // always read text first
    const json = safeParseJson(text);             // try to parse
    return { ok: res.ok, status: res.status, json, text, headers: res.headers };
  } catch (e) {
    return { ok: false, status: 0, json: null, text: String(e) };
  }
}

function decodeJwtCompact(jwt) {
  if (!jwt || typeof jwt !== "string" || jwt.split(".").length < 2) return null;
  const [h, p] = jwt.split(".");
  const b64ToStr = (s) => {
    s = s.replace(/-/g, "+").replace(/_/g, "/");
    const pad = s.length % 4 ? 4 - (s.length % 4) : 0;
    if (pad) s += "=".repeat(pad);
    try { return atob(s); } catch { return ""; }
  };
  const header = safeParseJson(b64ToStr(h));
  const payload = safeParseJson(b64ToStr(p));
  return { header, payload };
}

function setBusy(btn, busy) {
  if (!btn) return;
  btn.disabled = !!busy;
  if (busy) {
    btn.dataset._txt = btn.textContent;
    btn.textContent = "Processing…";
  } else if (btn.dataset._txt) {
    btn.textContent = btn.dataset._txt;
    delete btn.dataset._txt;
  }
}

function renderError(targetId, res) {
  const target = el(targetId);
  if (!target) return;
  const msg = (res && (res.json?.error || res.json?.message)) || res?.text || "Unexpected error";
  target.innerHTML = `❌ ${msg}`;
}

/* =========================================================
   Agent page: Issue e-Receipt
   ========================================================= */
if (el("btnIssue")) {
  el("btnIssue").onclick = async () => {
    const btn = el("btnIssue");
    setBusy(btn, true);
    el("issueResult").textContent = "";
    el("smsPreview").textContent = "—";

    const payload = {
      txnId: (el("txnId")?.value || "").trim(),
      msisdn: (el("msisdn")?.value || "").trim(),
      amount: parseFloat(el("amount")?.value || "0"),
      currency: (el("currency")?.value || "USD").trim(),
      items: [{
        sku: "SKU1",
        name: "Sample Product",
        qty: 1,
        price: parseFloat(el("amount")?.value || "0")
      }]
    };

    // simple client validation
    if (!payload.txnId || !payload.msisdn || isNaN(payload.amount)) {
      el("issueResult").textContent = "❌ Please fill Txn ID, MSISDN, and Amount.";
      setBusy(btn, false);
      return;
    }

    const res = await fetchSafe("/tcrm/issue", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    if (!res.ok || !res.json) {
      renderError("issueResult", res);
      setBusy(btn, false);
      return;
    }

    // success UI
    const j = res.json;
    let jwtHtml = "";
    if (j.jwt) {
      const decoded = decodeJwtCompact(j.jwt);
      if (decoded) {
        jwtHtml = `<details style="margin-top:8px">
          <summary>JWT (decoded)</summary>
          <pre style="white-space:pre-wrap">${JSON.stringify(decoded, null, 2)}</pre>
        </details>`;
      }
    }
    el("issueResult").innerHTML =
      `✅ Issued — receiptId: <b>${j.receiptId || "-"}</b>${jwtHtml}`;

    if (j.shortUrl) {
      el("smsPreview").innerHTML = `SMS: <a href="${j.shortUrl}" target="_blank" rel="noopener">${j.shortUrl}</a>`;
    } else {
      el("smsPreview").textContent = "No short URL returned.";
    }

    setBusy(btn, false);
  };
}

/* =========================================================
   Customer view page: Send/Verify OTP
   ========================================================= */
if (location.pathname.endsWith("/view.html")) {
  const params = new URLSearchParams(location.search);
  const token = params.get("token");

  const statusEl = el("status");
  const otpDemoEl = el("otpDemo");

  const requireToken = () => {
    if (!token) {
      if (statusEl) statusEl.textContent = "❌ Missing token in URL.";
      return false;
    }
    return true;
  };

  const btnSend = el("sendOtp");
  if (btnSend) {
    btnSend.onclick = async () => {
      if (!requireToken()) return;
      setBusy(btnSend, true);
      if (otpDemoEl) otpDemoEl.innerHTML = "";
      if (statusEl) statusEl.textContent = "";

      const res = await fetchSafe("/api/otp/send", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token })
      });

      if (!res.ok || !res.json) {
        renderError("status", res);
        setBusy(btnSend, false);
        return;
      }

      // demo mode may return the OTP
      if (res.json.otpDemo && otpDemoEl) {
        otpDemoEl.innerHTML = `<small>DEMO OTP: <b>${res.json.otpDemo}</b></small>`;
      } else if (statusEl) {
        statusEl.textContent = "OTP sent.";
      }
      setBusy(btnSend, false);
    };
  }

  const btnVerify = el("verifyOtp");
  if (btnVerify) {
    btnVerify.onclick = async () => {
      if (!requireToken()) return;
      setBusy(btnVerify, true);
      if (statusEl) statusEl.textContent = "";

      const code = (el("otpCode")?.value || "").trim();
      if (!code) {
        if (statusEl) statusEl.textContent = "❌ Enter the OTP code.";
        setBusy(btnVerify, false);
        return;
      }

      const res = await fetchSafe("/api/otp/verify", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token, code })
      });

      if (!res.ok || !res.json) {
        renderError("status", res);
        setBusy(btnVerify, false);
        return;
      }

      const rid = res.json.receiptId;
      if (!rid) {
        if (statusEl) statusEl.textContent = "❌ Missing receipt ID from server.";
        setBusy(btnVerify, false);
        return;
      }

      if (statusEl) statusEl.textContent = "✅ Verified. Opening receipt…";
      // navigate to receipt page
      window.location.href = `/receipt.html#${rid}`;
    };
  }
}

/* =========================================================
   Receipt page: Fetch & render receipt
   ========================================================= */
if (location.pathname.endsWith("/receipt.html")) {
  (async () => {
    const rid = (location.hash || "").replace(/^#/, "");
    const container = document.getElementById("receiptContainer");
    if (!container) return;

    if (!rid) {
      container.innerHTML = `<section class="card">❌ Missing receipt ID.</section>`;
      return;
    }

    const res = await fetchSafe(`/api/receipt/${encodeURIComponent(rid)}`);
    if (!res.ok || !res.json) {
      container.innerHTML = `<section class="card">❌ ${(res.json?.error || res.text || "Failed to load receipt")}</section>`;
      return;
    }

    const j = res.json;
    const items = Array.isArray(j.items) ? j.items : [];
    const rows = items.map(i => `
      <tr>
        <td>${(i.name ?? "").toString()}</td>
        <td style="text-align:center">${Number(i.qty ?? 0)}</td>
        <td style="text-align:right">${Number(i.price ?? 0).toFixed(2)}</td>
      </tr>
    `).join("");

    container.className = "card";
    container.innerHTML = `
      <h2>Receipt #${j.receiptId}</h2>
      <p><b>Txn:</b> ${j.txnId} &nbsp; <b>MSISDN:</b> ${j.msisdn}</p>
      <p><b>Amount:</b> ${Number(j.amount ?? 0).toFixed(2)} ${j.currency || ""}</p>
      <table border="0" cellpadding="6">
        <thead><tr><th align="left">Item</th><th>Qty</th><th>Price</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
      <p><small>Valid until: ${j.expiresAt ? new Date(j.expiresAt).toLocaleString() : "-"}</small></p>
      <button id="downloadPdf">Download PDF (client-side)</button>
    `;

    const btn = document.getElementById("downloadPdf");
    if (btn) btn.onclick = () => window.print();
  })();
}
