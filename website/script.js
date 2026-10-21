"use strict";

const screens = {
  overview: { caption: "Your key resources and their top consumers, together.", alt: "ARMpulse Overview with CPU and memory charts, network and power, and top CPU and memory process lists" },
  processes: { caption: "Search, sort, and find the process behind the activity.", alt: "ARMpulse Processes with searchable running processes, CPU, memory, I/O and architecture columns" },
  memory: { caption: "Physical memory, available capacity, and recent history.", alt: "ARMpulse Memory with physical RAM in use, available memory, and a memory history chart" },
  history: { caption: "A little context for every spike, kept in this session.", alt: "ARMpulse History with average and peak CPU, peak memory, and the CPU and memory timeline" },
  settings: { caption: "Set the pace. Keep your preferences on your own PC.", alt: "ARMpulse Settings with the refresh interval, always-on-top option, architecture information and app details" }
};

const tabs = [...document.querySelectorAll("[data-screen]")];
const image = document.getElementById("app-screenshot");
const panel = document.getElementById("app-panel");
const caption = document.getElementById("screen-caption");

function selectScreen(tab, moveFocus = false) {
  const screen = screens[tab.dataset.screen];
  if (!screen) return;
  tabs.forEach(item => {
    const selected = item === tab;
    item.setAttribute("aria-selected", String(selected));
    item.tabIndex = selected ? 0 : -1;
  });
  panel.setAttribute("aria-labelledby", tab.id);
  panel.setAttribute("aria-busy", "true");
  image.alt = screen.alt;
  image.src = tab.getAttribute("href");
  caption.textContent = screen.caption;
  if (image.complete) panel.removeAttribute("aria-busy");
  if (moveFocus) tab.focus();
}

image.addEventListener("load", () => panel.removeAttribute("aria-busy"));
image.addEventListener("error", () => {
  panel.removeAttribute("aria-busy");
  caption.textContent = "This screenshot could not load. Open the screenshot link in a new tab to try again.";
});

tabs.forEach((tab, index) => {
  tab.addEventListener("click", event => {
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    selectScreen(tab);
  });
  tab.addEventListener("keydown", event => {
    let next;
    if (event.key === "ArrowRight") next = (index + 1) % tabs.length;
    else if (event.key === "ArrowLeft") next = (index - 1 + tabs.length) % tabs.length;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = tabs.length - 1;
    else return;
    event.preventDefault();
    selectScreen(tabs[next], true);
  });
});
