<template>
  <main style="font-family: system-ui, sans-serif; margin: 2rem auto; max-width: 720px">
    <h1>HybridApp Vue Template</h1>
    <p style="margin-bottom: 1rem">
      Bridge:
      <span v-if="error" style="color: #e74c3c">{{ error }}</span>
      <span v-else-if="ready" style="color: #27ae60">connected</span>
      <span v-else style="color: #f39c12">connecting…</span>
    </p>

    <section v-if="ready">
      <h2>Greeter Service</h2>
      <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem">
        <input
          v-model="name"
          type="text"
          placeholder="Enter your name..."
          style="padding: 0.5rem; flex: 1; border-radius: 4px; border: 1px solid #ccc"
        />
        <button @click="handleGreet" style="padding: 0.5rem 1rem">Greet from C#</button>
      </div>
      <p v-if="greeting">{{ greeting }}</p>
    </section>
  </main>
</template>

<script setup lang="ts">
import { ref } from "vue";
import { useBridgeReady } from "./composables/useBridge";
import { services } from "./bridge/client";

const { ready, error } = useBridgeReady();
const name = ref("");
const greeting = ref<string | null>(null);

async function handleGreet() {
  try {
    greeting.value = await services.greeter.greet({ name: name.value || "World" });
  } catch (err) {
    greeting.value = `Error: ${(err as Error).message}`;
  }
}
</script>
