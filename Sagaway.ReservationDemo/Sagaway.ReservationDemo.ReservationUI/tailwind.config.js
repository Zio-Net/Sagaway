/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    // Ensure this pattern correctly finds your Blazor files
    './**/*.{razor,html,cshtml}'
  ],
  theme: {
    extend: {},
  },
  plugins: [],
}