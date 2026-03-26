import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'MonadicSharp.AI',
  description: 'AI-specific extensions for MonadicSharp — typed error handling, retry, tracing, and streaming for LLM pipelines.',
  base: '/MonadicSharp.AI/',
  cleanUrls: true,

  head: [
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { name: 'twitter:card', content: 'summary' }],
  ],

  themeConfig: {
    logo: '/logo.svg',
    siteTitle: 'MonadicSharp.AI',

    nav: [
      {
        text: 'Guide',
        items: [
          { text: 'Getting Started', link: '/getting-started' },
          { text: 'Why MonadicSharp.AI?', link: '/why' },
        ],
      },
      {
        text: 'API',
        items: [
          { text: 'AiError', link: '/api/ai-error' },
          { text: 'RetryResult<T>', link: '/api/retry-result' },
          { text: 'ValidatedResult<T>', link: '/api/validated-result' },
          { text: 'AgentResult<T>', link: '/api/agent-result' },
          { text: 'StreamResult', link: '/api/stream-result' },
        ],
      },
      {
        text: 'Ecosystem',
        items: [
          {
            text: 'Core',
            items: [
              { text: 'MonadicSharp', link: 'https://danny4897.github.io/MonadicSharp/' },
              { text: 'MonadicSharp.Framework', link: 'https://danny4897.github.io/MonadicSharp.Framework/' },
            ],
          },
          {
            text: 'Extensions',
            items: [
              { text: 'MonadicSharp.AI', link: 'https://danny4897.github.io/MonadicSharp.AI/' },
              { text: 'MonadicSharp.Recovery', link: 'https://danny4897.github.io/MonadicSharp.Recovery/' },
              { text: 'MonadicSharp.Azure', link: 'https://danny4897.github.io/MonadicSharp.Azure/' },
              { text: 'MonadicSharp.DI', link: 'https://danny4897.github.io/MonadicSharp.DI/' },
            ],
          },
          {
            text: 'Tooling',
            items: [
              { text: 'MonadicLeaf', link: 'https://danny4897.github.io/MonadicLeaf/' },
              { text: 'MonadicSharp × OpenCode', link: 'https://danny4897.github.io/MonadicSharp-OpenCode/' },
              { text: 'AgentScope', link: 'https://danny4897.github.io/AgentScope/' },
            ],
          },
        ],
      },
    ],

    sidebar: {
      '/': [
        {
          text: 'Guide',
          items: [
            { text: 'Getting Started', link: '/getting-started' },
            { text: 'Why MonadicSharp.AI?', link: '/why' },
          ],
        },
        {
          text: 'API Reference',
          items: [
            { text: 'AiError', link: '/api/ai-error' },
            { text: 'RetryResult<T>', link: '/api/retry-result' },
            { text: 'ValidatedResult<T>', link: '/api/validated-result' },
            { text: 'AgentResult<T>', link: '/api/agent-result' },
            { text: 'StreamResult', link: '/api/stream-result' },
          ],
        },
      ],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/Danny4897/MonadicSharp.AI' },
    ],

    search: { provider: 'local' },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2024–2026 Danny4897',
    },

    outline: { level: [2, 3], label: 'On this page' },
  },

  markdown: {
    theme: { light: 'github-light', dark: 'one-dark-pro' },
  },
})
