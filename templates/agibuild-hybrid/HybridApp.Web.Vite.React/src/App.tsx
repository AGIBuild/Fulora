import { useState } from "react";
import { useBridgeReady } from "./hooks/useBridge";
import { services } from "./bridge/client";

export function App() {
  const { ready, error } = useBridgeReady();
  const [name, setName] = useState("");
  const [greeting, setGreeting] = useState<string | null>(null);

  async function handleGreet() {
    try {
      const result = await services.greeter.greet({ name: name || "World" });
      setGreeting(result);
    } catch (err) {
      setGreeting(`Error: ${(err as Error).message}`);
    }
  }

  return (
    <main style={{ fontFamily: "system-ui, sans-serif", margin: "2rem auto", maxWidth: 720 }}>
      <h1>HybridApp React Template</h1>
      <p style={{ marginBottom: "1rem" }}>
        Bridge:{" "}
        {error ? (
          <span style={{ color: "#e74c3c" }}>{error}</span>
        ) : ready ? (
          <span style={{ color: "#27ae60" }}>connected</span>
        ) : (
          <span style={{ color: "#f39c12" }}>connecting…</span>
        )}
      </p>

      {ready && (
        <section>
          <h2>Greeter Service</h2>
          <div style={{ display: "flex", gap: "0.5rem", marginBottom: "0.5rem" }}>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Enter your name..."
              style={{ padding: "0.5rem", flex: 1, borderRadius: 4, border: "1px solid #ccc" }}
            />
            <button onClick={handleGreet} style={{ padding: "0.5rem 1rem" }}>
              Greet from C#
            </button>
          </div>
          {greeting && <p>{greeting}</p>}
        </section>
      )}
    </main>
  );
}
