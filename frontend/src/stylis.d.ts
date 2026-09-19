// stylis and stylis-plugin-rtl ship without TypeScript declarations.
declare module 'stylis' {
  export const prefixer: (element: unknown, index: number, children: unknown[], callback: unknown) => string | void;
}
declare module 'stylis-plugin-rtl' {
  const plugin: (element: unknown, index: number, children: unknown[], callback: unknown) => string | void;
  export default plugin;
}
