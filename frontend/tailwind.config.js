import tailwindcssAnimate from "tailwindcss-animate";

/** @type {import('tailwindcss').Config} */
export default {
  darkMode: ["class"],
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    container: {
      center: true,
      padding: "2rem",
      screens: {
        "2xl": "1400px",
      },
    },
    extend: {
      colors: {
        border: "hsl(var(--border))",
        input: "hsl(var(--input))",
        ring: "hsl(var(--ring))",
        background: "hsl(var(--background))",
        foreground: "hsl(var(--foreground))",
        primary: {
          DEFAULT: "hsl(var(--primary))",
          foreground: "hsl(var(--primary-foreground))",
        },
        secondary: {
          DEFAULT: "hsl(var(--secondary))",
          foreground: "hsl(var(--secondary-foreground))",
        },
        destructive: {
          DEFAULT: "hsl(var(--destructive))",
          foreground: "hsl(var(--destructive-foreground))",
        },
        muted: {
          DEFAULT: "hsl(var(--muted))",
          foreground: "hsl(var(--muted-foreground))",
        },
        accent: {
          DEFAULT: "hsl(var(--accent))",
          foreground: "hsl(var(--accent-foreground))",
        },
        popover: {
          DEFAULT: "hsl(var(--popover))",
          foreground: "hsl(var(--popover-foreground))",
        },
        card: {
          DEFAULT: "hsl(var(--card))",
          foreground: "hsl(var(--card-foreground))",
        },
        nau: {
          bg: "#0a0a0a",
          "bg-alt": "#0f0f10",
          fg: "#f5f5f4",
          "fg-dim": "#888885",
          line: "rgba(255,255,255,0.10)",
          "line-strong": "rgba(255,255,255,0.20)",
          accent: "#facc15",
          "accent-2": "#f472b6",
          blue: "#60a5fa",
          danger: "#f472b6",
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'Segoe UI', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', '"SF Mono"', 'Menlo', 'Consolas', 'monospace'],
      },
      letterSpacing: {
        mono: "0.12em",
        "mono-wide": "0.14em",
        "mono-xwide": "0.18em",
      },
      borderRadius: {
        lg: "var(--radius)",
        md: "calc(var(--radius) - 2px)",
        sm: "calc(var(--radius) - 4px)",
      },
      keyframes: {
        "accordion-down": {
          from: { height: "0" },
          to: { height: "var(--radix-accordion-content-height)" },
        },
        "accordion-up": {
          from: { height: "var(--radix-accordion-content-height)" },
          to: { height: "0" },
        },
        "nau-pulse": {
          "0%, 100%": { opacity: "1" },
          "50%": { opacity: "0.35" },
        },
        "nau-blink": {
          "0%, 49%": { opacity: "1" },
          "50%, 100%": { opacity: "0" },
        },
        "nau-mech-open": {
          "0%": { transform: "scaleX(0) scaleY(0)" },
          "30%": { transform: "scaleX(1) scaleY(0.04)" },
          "100%": { transform: "scaleX(1) scaleY(1)" },
        },
        "nau-mech-fade": {
          "0%, 55%": { opacity: "0" },
          "100%": { opacity: "1" },
        },
        "page-in": {
          from: { opacity: "0", transform: "translateY(8px)" },
          to: { opacity: "1", transform: "translateY(0)" },
        },
      },
      animation: {
        "accordion-down": "accordion-down 0.2s ease-out",
        "accordion-up": "accordion-up 0.2s ease-out",
        "nau-pulse": "nau-pulse 1.2s ease-in-out infinite",
        "nau-blink": "nau-blink 0.8s steps(1) infinite",
        "nau-mech-open": "nau-mech-open 320ms cubic-bezier(0.85, 0, 0.15, 1) both",
        "nau-mech-fade": "nau-mech-fade 320ms linear both",
        "page-in": "page-in 180ms ease-out",
      },
    },
  },
  plugins: [tailwindcssAnimate],
};
