/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{html,ts}",
  ],
  theme: {
    extend: {
      colors: {
        legal: {
          50: '#f4f6fa',
          100: '#eaecf4',
          200: '#d0d6e7',
          300: '#a6b2d2',
          400: '#768ab7',
          500: '#526b9c',
          600: '#3f5380',
          700: '#334368',
          800: '#2d3856',
          950: '#0c0f18',
        }
      }
    },
  },
  plugins: [],
}
