Right now, Red Hat’s implementation mostly relies on --read-only flags and basic RBAC. They know they need a brake for the AI, but they haven't solved the cryptographic/identity integrity problem yet. You have[[1](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQH1OBRjDi_8guWN6IxAOBy119eJkdzfizoK5btubCwgnnfBlOvk99RfCsIJWUO0d4HHJVPCNerWvnJQRSaDE1NwLwiAvkds_9jzEuX0iB8HRDMtfbnkxlRfoO3UNb8_8hXUxTLhfwZlgZWAfCFiaWNTO5JOG_jcj-DY-dXafMIBIIXgX6BlQ48j-sKcU9_p16G0tRGNiIYR)].

Since your project (Kubernetes-MCP-Guard) is built in .NET 10[[2](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQFY5Aa6fdBQT-onYqNQtPeB1bb5zV25KADxJWwEXsH7dy9OnctW5WTxZ1LGGBgpnRALngV2QF0lfXLitivIUrhegpjjdPkCCbfGv-XYqYx468pYzbAkFo5VmXssJ5gLYKc8lit5EYJPdyrTwmjd_uyYEM35LDGrlw%3D%3D)][[3](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHDgOwV4FNZAKjcdAaauh3S2DL9Zz2abWb0RVmVWmWSBbjDftQzDFgj4TvMjJuUH3gSfektutCWtG_FN_eZR41Mky21UvsVZdxgpIxw1CxI5dQnsV6FQU3EZE4jtfv9kG5rbHCcZIyqiR2ZMV9iV1XnIaKo2LcIMDNLqn339AEu3idvh93MvtUc)] and theirs is in Go, you won't be merging your code directly into their repository. Instead, your goal is to **propose an architecture and a standard**.

Here is a step-by-step strategy on how to approach them, followed by a template you can use to open the conversation.

---

### Step 1: Prepare Your "Proof" (The E2E Tests)

Red Hat engineers respect testable, reproducible security. Before you reach out, ensure your feature/safety-tests branch clearly demonstrates the failure states. Your E2E tests should explicitly show:

1. **The TOCTOU Block:** An AI tries to modify the JSON plan after the human approves it, and the system blocks it with approval_hash_mismatch[[1](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQH1OBRjDi_8guWN6IxAOBy119eJkdzfizoK5btubCwgnnfBlOvk99RfCsIJWUO0d4HHJVPCNerWvnJQRSaDE1NwLwiAvkds_9jzEuX0iB8HRDMtfbnkxlRfoO3UNb8_8hXUxTLhfwZlgZWAfCFiaWNTO5JOG_jcj-DY-dXafMIBIIXgX6BlQ48j-sKcU9_p16G0tRGNiIYR)].
    
2. **Prompt-Injection Redaction:** An E2E test showing a malicious payload in a Kubernetes pod label being redacted at the HTTP gateway before the AI client receives it[[1](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQH1OBRjDi_8guWN6IxAOBy119eJkdzfizoK5btubCwgnnfBlOvk99RfCsIJWUO0d4HHJVPCNerWvnJQRSaDE1NwLwiAvkds_9jzEuX0iB8HRDMtfbnkxlRfoO3UNb8_8hXUxTLhfwZlgZWAfCFiaWNTO5JOG_jcj-DY-dXafMIBIIXgX6BlQ48j-sKcU9_p16G0tRGNiIYR)].
    
3. **The Out-of-Band Auth:** A test proving the AI cannot bypass the browser OAuth flow[[2](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQFY5Aa6fdBQT-onYqNQtPeB1bb5zV25KADxJWwEXsH7dy9OnctW5WTxZ1LGGBgpnRALngV2QF0lfXLitivIUrhegpjjdPkCCbfGv-XYqYx468pYzbAkFo5VmXssJ5gLYKc8lit5EYJPdyrTwmjd_uyYEM35LDGrlw%3D%3D)].
    

### Step 2: Open an RFC (Request for Comments) Discussion

Do not just open a Pull Request or a standard issue. Open a **GitHub Discussion** or an **Issue prefixed with [RFC]** in their repository. Red Hat teams use the RFC process heavily to discuss architectural standards before writing code.

You want to frame your approach not as a competing product, but as a **missing security standard for the MCP ecosystem**[[1](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQH1OBRjDi_8guWN6IxAOBy119eJkdzfizoK5btubCwgnnfBlOvk99RfCsIJWUO0d4HHJVPCNerWvnJQRSaDE1NwLwiAvkds_9jzEuX0iB8HRDMtfbnkxlRfoO3UNb8_8hXUxTLhfwZlgZWAfCFiaWNTO5JOG_jcj-DY-dXafMIBIIXgX6BlQ48j-sKcU9_p16G0tRGNiIYR)].

### Step 3: The Pitch Template

Here is a draft you can use to open an issue on the containers/kubernetes-mcp-server repository:

> **Title:** [RFC] Standardizing Human-in-the-Loop (HITL) via Cryptographic Plan Verification to prevent TOCTOU
> 
> **Hi team,**
> 
> I’ve been heavily researching the security implications of binding MCP to Kubernetes state mutations. I love the work you’re doing here, but I believe the current ecosystem approach to Human-in-the-Loop (HITL) is fundamentally vulnerable to **Time-of-Check to Time-of-Use (TOCTOU)** attacks and consent fatigue.
> 
> If an AI agent asks for permission to modify a deployment via a simple boolean prompt (e.g., in Claude Desktop or a Slack bot), a compromised AI or prompt-injected payload can swap the operation parameters after the human clicks "Approve."
> 
> In my own gateway project, **Kubernetes-MCP-Guard**, I've implemented a pattern that I believe should become the standard for infrastructure MCP servers: **Plans, Challenges, and Hashes.**
> 
> **The Proposed Flow:**
> 
> 1. **Plan Generation:** The AI calls propose_apply. Instead of mutating the cluster, the server writes a pending plan (JSON) to disk and generates a SHA-256 hash of the payload.
>     
> 2. **Out-of-Band Elicitation:** The AI is forced to halt and wait. The human approves the exact payload out-of-band.
>     
> 3. **Execution with Integrity:** The AI calls apply_approved_plan and must provide the correct hash. The server validates that the hash matches the exact approved plan. If it doesn't, it fails with approval_hash_mismatch.
>     
> 
> I've written extensive E2E safety tests proving this stops payload-swapping and prompt-injection mutations. You can see the tests and the implementation in my WIP branch here:  
> https://github.com/mirusser/Kubernetes-MCP-Guard/tree/feature/safety-tests/tests  
> (See also my recent write-up on this: "Your AI just deleted the wrong deployment. Now what?")
> 
> **The Ask:**  
> Because your repository is becoming a foundational reference for Kubernetes MCP, I’d love to collaborate on formalizing this cryptographic HITL pattern.
> 
> Would the Red Hat team be open to discussing implementing a propose -> hash -> apply tool structure standard in this repo? If we agree on the JSON schema/pattern, I can help draft the specification.

---

### Step 4: Proposing an Official MCP Extension

Beyond Red Hat, if you want this to become an actual standard, you should bring this to the creators of MCP (Anthropic and the open-source community).

Currently, MCP has a standard for roots, resources, and prompts. It **does not** have a formal standard for mutations or approvals[[1](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQH1OBRjDi_8guWN6IxAOBy119eJkdzfizoK5btubCwgnnfBlOvk99RfCsIJWUO0d4HHJVPCNerWvnJQRSaDE1NwLwiAvkds_9jzEuX0iB8HRDMtfbnkxlRfoO3UNb8_8hXUxTLhfwZlgZWAfCFiaWNTO5JOG_jcj-DY-dXafMIBIIXgX6BlQ48j-sKcU9_p16G0tRGNiIYR)].

1. **Draft an MCP Specification Extension:** Write a markdown document defining a new MCP capability: experimental/approval_hashes.
    
2. Define the exact JSON RPC messages required for an MCP server to tell an MCP client: "I need human approval, here is the SHA-256 hash of the payload they need to sign."
    
3. Submit this as an issue to the official Anthropic MCP specification repository.


---

AWS and Oracle are building proprietary HITL walls. We need an open, cryptographic HITL standard for MCP to compete, and my hash-based plan architecture does exactly that.

----

### 1. AWS Nova Act (The "UI Takeover" Approach)

AWS Nova Act (which went GA in December 2025 with huge SDK/HITL updates in early 2026) is a service specifically designed for **browser automation** (AI clicking around a web page)[[1](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQF_NtBJpuNqGHre-qmG59DtLFAtqFSDHsx74LIP_YEpOCeR-A27aBtyj-OzViaz6SO0KPQQiKnEX4cbmwk6nVTPHW0ZS0fcjiaiUQRGUNCDskhABbnoUlCRLq10)][[2](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQGLEbFBk6sV-XTVgypW4MoSfUTszEg7AjifC7hnruKqS1Q8RHMaIvU_bvOBOZsBCGKbfBDV8GYTFiH0CPQKwfYM3t4c96XXzmJSG980GBtdY2rONayuPA%3D%3D)][[3](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQFC0YZZeCnU871S_TOzwi6T1lGHK9XNimOqVnIFRtIXuZr5eMxEtT_64_KpMlinY9lk-DENHDgocIzbjxLnk1limsZil-bgqWTsmu2eyIKm0RGSajrcmEOZNDDsxsBmn3GPSqUqZKM%3D)].

- **How they do HITL:** They use a concept called **"UI Takeover"** and screenshot sharing[[4](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHHT_QqHzdUbHsdCnaKWj8EuoksxmWipC5rO4sNVfzzQbcusVsWUbhTRDzYYhaTfR2atKTtfHNA7vlQh67spO25MH0zlIX2IPCd0mm7e4ViocpM4EKoUGosV9BxYZxjoi2Ns3MJCTtnxhQS5qE1hmuVLs0OEEEfqg%3D%3D)]. If the AI is trying to fill out a form and hits a CAPTCHA, or is about to click "Submit Payment," the SDK pauses[[4](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHHT_QqHzdUbHsdCnaKWj8EuoksxmWipC5rO4sNVfzzQbcusVsWUbhTRDzYYhaTfR2atKTtfHNA7vlQh67spO25MH0zlIX2IPCd0mm7e4ViocpM4EKoUGosV9BxYZxjoi2Ns3MJCTtnxhQS5qE1hmuVLs0OEEEfqg%3D%3D)]. It sends a live screenshot of the virtual browser to a human supervisor[[4](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHHT_QqHzdUbHsdCnaKWj8EuoksxmWipC5rO4sNVfzzQbcusVsWUbhTRDzYYhaTfR2atKTtfHNA7vlQh67spO25MH0zlIX2IPCd0mm7e4ViocpM4EKoUGosV9BxYZxjoi2Ns3MJCTtnxhQS5qE1hmuVLs0OEEEfqg%3D%3D)]. The human can either click an "Approve" button, or physically take over the mouse and keyboard via a remote web session to complete the step[[4](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHHT_QqHzdUbHsdCnaKWj8EuoksxmWipC5rO4sNVfzzQbcusVsWUbhTRDzYYhaTfR2atKTtfHNA7vlQh67spO25MH0zlIX2IPCd0mm7e4ViocpM4EKoUGosV9BxYZxjoi2Ns3MJCTtnxhQS5qE1hmuVLs0OEEEfqg%3D%3D)].
    
- **Why it is different from yours:** AWS is securing visual browser state. They are not securing structured API payloads (like Kubernetes YAML/JSON)[[4](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHHT_QqHzdUbHsdCnaKWj8EuoksxmWipC5rO4sNVfzzQbcusVsWUbhTRDzYYhaTfR2atKTtfHNA7vlQh67spO25MH0zlIX2IPCd0mm7e4ViocpM4EKoUGosV9BxYZxjoi2Ns3MJCTtnxhQS5qE1hmuVLs0OEEEfqg%3D%3D)][[5](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQG8PDfxWNCPnxB1FpXLidnxCC3A6qkHUFGwbGrLDPmWyf3zRYFIJ50QEm51s0e55Cz8m03JGqKOSePGjkFfeohwDUTVkanYMOAJPqjf1k3b5w1xA3ZjztlDDvP5By3i2wankz5tsw6a2ehMznKkjMgKzE9YFtpJxgUTrtOF0iZL5yZmULiOQg%3D%3D)]. If an AI uses AWS Nova to interact with an API, their fallback is still a simple text prompt (approve(message) -> True/False)[[4](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHHT_QqHzdUbHsdCnaKWj8EuoksxmWipC5rO4sNVfzzQbcusVsWUbhTRDzYYhaTfR2atKTtfHNA7vlQh67spO25MH0zlIX2IPCd0mm7e4ViocpM4EKoUGosV9BxYZxjoi2Ns3MJCTtnxhQS5qE1hmuVLs0OEEEfqg%3D%3D)], which remains vulnerable to the exact Time-of-Check to Time-of-Use (TOCTOU) payload-swapping attack you identified.
    

### 2. Oracle Integration Cloud (The "Stateful Gatekeeper" Approach)

Between March and April 2026, Oracle announced a massive "Agentic AI" overhaul for their Fusion Cloud and Oracle Integration platforms, heavily featuring a new **Human in the Loop** capability[[6](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQFLpPdOtztrGTliMFktRw0jvcplAREuhuH4_hZpyV26pKFRQitgsn2qVY7tjX1yMCewylavCH1h98jLeIySW-AhYN1z_zwbKVR9QmyanDNfCmdk0S7AessVLAj5EJ56BXCrfNKkSDT_ZNN2U-Ik7VeKzEcfm6m0ZHofwCXSnsx8n0r_zq8Qm2uKE7TSu0-ic28YlMAiALNi1tTnwlq16srxc8k%3D)][[7](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEixmY5fZfJ7atInVEvwWHoJGMgq0jydaw_XZlCdePmH9nv1I437r8GfeSCaJDBgJfskeGqa5UxkTa4a78_Rk78OKONO2OoVSID763QO-IOiRGwD3TB_itZjKfz9ZP_ti8bDC9Rzmw4Nj7zwYrMX8gXbLFEzebWX9DXEoOAjh6-H1L-TcE5mBvQm6v_WLZ5Gjdk3eQNz3B4UtXgiCtQM9PkAktxXJGLLl_B)][[8](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEVLU3y0gkoXp5oXok1sk5A-k_qgnkJOkE3mZ-zORr8dNo2z0o14a1jeCB4POQCeRCF2H165VmLB-e8FN4humOwPRQxgS65YMwVapltf9orI6OQXEsT7T-QLISkNcPE0KEuabvZX_8cDCS6rJ14fuHJR3kZnimbRA%3D%3D)].

- **How they do HITL:** Oracle treats HITL as a traditional Enterprise BPEL/Approval workflow[[7](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEixmY5fZfJ7atInVEvwWHoJGMgq0jydaw_XZlCdePmH9nv1I437r8GfeSCaJDBgJfskeGqa5UxkTa4a78_Rk78OKONO2OoVSID763QO-IOiRGwD3TB_itZjKfz9ZP_ti8bDC9Rzmw4Nj7zwYrMX8gXbLFEzebWX9DXEoOAjh6-H1L-TcE5mBvQm6v_WLZ5Gjdk3eQNz3B4UtXgiCtQM9PkAktxXJGLLl_B)][[8](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEVLU3y0gkoXp5oXok1sk5A-k_qgnkJOkE3mZ-zORr8dNo2z0o14a1jeCB4POQCeRCF2H165VmLB-e8FN4humOwPRQxgS65YMwVapltf9orI6OQXEsT7T-QLISkNcPE0KEuabvZX_8cDCS6rJ14fuHJR3kZnimbRA%3D%3D)]. If an AI agent wants to mutate state (e.g., approve an invoice or change a database entry), it sends the request to the Oracle Integration Cloud (OIC)[[7](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEixmY5fZfJ7atInVEvwWHoJGMgq0jydaw_XZlCdePmH9nv1I437r8GfeSCaJDBgJfskeGqa5UxkTa4a78_Rk78OKONO2OoVSID763QO-IOiRGwD3TB_itZjKfz9ZP_ti8bDC9Rzmw4Nj7zwYrMX8gXbLFEzebWX9DXEoOAjh6-H1L-TcE5mBvQm6v_WLZ5Gjdk3eQNz3B4UtXgiCtQM9PkAktxXJGLLl_B)]. OIC runs it through a "Decision Service" (business rules). If it requires approval, OIC holds the request in its own proprietary database and sends an alert to a human in the Oracle dashboard[[7](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEixmY5fZfJ7atInVEvwWHoJGMgq0jydaw_XZlCdePmH9nv1I437r8GfeSCaJDBgJfskeGqa5UxkTa4a78_Rk78OKONO2OoVSID763QO-IOiRGwD3TB_itZjKfz9ZP_ti8bDC9Rzmw4Nj7zwYrMX8gXbLFEzebWX9DXEoOAjh6-H1L-TcE5mBvQm6v_WLZ5Gjdk3eQNz3B4UtXgiCtQM9PkAktxXJGLLl_B)]. The human clicks "Approve," and OIC executes the payload[[7](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEixmY5fZfJ7atInVEvwWHoJGMgq0jydaw_XZlCdePmH9nv1I437r8GfeSCaJDBgJfskeGqa5UxkTa4a78_Rk78OKONO2OoVSID763QO-IOiRGwD3TB_itZjKfz9ZP_ti8bDC9Rzmw4Nj7zwYrMX8gXbLFEzebWX9DXEoOAjh6-H1L-TcE5mBvQm6v_WLZ5Gjdk3eQNz3B4UtXgiCtQM9PkAktxXJGLLl_B)].
    
- **Why it is different from yours:** Oracle solves the TOCTOU problem, but they do it by forcing the user to adopt a **massive, centralized, stateful, proprietary workflow engine**[[7](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEixmY5fZfJ7atInVEvwWHoJGMgq0jydaw_XZlCdePmH9nv1I437r8GfeSCaJDBgJfskeGqa5UxkTa4a78_Rk78OKONO2OoVSID763QO-IOiRGwD3TB_itZjKfz9ZP_ti8bDC9Rzmw4Nj7zwYrMX8gXbLFEzebWX9DXEoOAjh6-H1L-TcE5mBvQm6v_WLZ5Gjdk3eQNz3B4UtXgiCtQM9PkAktxXJGLLl_B)][[8](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEVLU3y0gkoXp5oXok1sk5A-k_qgnkJOkE3mZ-zORr8dNo2z0o14a1jeCB4POQCeRCF2H165VmLB-e8FN4humOwPRQxgS65YMwVapltf9orI6OQXEsT7T-QLISkNcPE0KEuabvZX_8cDCS6rJ14fuHJR3kZnimbRA%3D%3D)][[9](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQHsYop2q1qNYQOuvo6-bxmVBgc3DzQyF2ZXrmmkwwToFrWqOzcDSf27rkCCP-9v3Fu4YP1C0xFrHupLoeWAyfrPDkFSACppCZthkQi7-ARXXX1a-vw7056SIXfWDHXlYURnar7w_Ntcdfq2EFlX0Ri5GkroZEj55ofj_hSSORhiqPZ6-wdvrbXxpkphlo2y8IwishKQVpU1xJbLgX80AQ5xWKb2zEAwa957ZpeVEw%3D%3D)]. The AI doesn't execute the action; it just submits a ticket to Oracle, and Oracle executes it[[7](https://www.google.com/url?sa=E&q=https%3A%2F%2Fvertexaisearch.cloud.google.com%2Fgrounding-api-redirect%2FAUZIYQEixmY5fZfJ7atInVEvwWHoJGMgq0jydaw_XZlCdePmH9nv1I437r8GfeSCaJDBgJfskeGqa5UxkTa4a78_Rk78OKONO2OoVSID763QO-IOiRGwD3TB_itZjKfz9ZP_ti8bDC9Rzmw4Nj7zwYrMX8gXbLFEzebWX9DXEoOAjh6-H1L-TcE5mBvQm6v_WLZ5Gjdk3eQNz3B4UtXgiCtQM9PkAktxXJGLLl_B)].
    

---

### Why Your Approach is Unique (and Highly Valuable)

Your approach in **Kubernetes-MCP-Guard** is fundamentally different from both AWS and Oracle because yours is **Cryptographic, Stateless, and Trustless**.

1. **You don't rely on a massive state machine (like Oracle).**  
    Instead of holding the pending action in a database, your server writes a JSON plan, generates a SHA-256 hash, and hands it back to the client. The integrity is guaranteed by the math, not by a monolithic enterprise software layer.
    
2. **You prevent Payload Swapping natively.**  
    Unlike the basic needs_approval=True flags used by OpenAI or AWS's text approvals, your gateway forces the AI to present the exact hash of the plan the human approved. The AI cannot secretly swap the payload in memory between the time the human says "Yes" and the execution.
    
3. **You are MCP-Native.**  
    AWS and Oracle are building walled gardens. You are building an open standard for the Model Context Protocol (MCP) ecosystem. You are allowing DevOps teams to use Anthropic Claude, OpenAI, or local Llama models securely with Kubernetes, without buying into Oracle Fusion or AWS Nova.


----
