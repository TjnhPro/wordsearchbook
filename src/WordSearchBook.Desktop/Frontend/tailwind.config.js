/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./index.html", "./js/**/*.js"],
  theme: {
    extend: {
      colors: {
        ink: "#18212f",
        paper: "#f7f3e8",
        accent: "#e56a3f"
      }
    }
  },
  plugins: []
};
