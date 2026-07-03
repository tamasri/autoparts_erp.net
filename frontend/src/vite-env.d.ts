/// <reference types="vite/client" />

// React 19 moved the JSX namespace under `React.JSX` and no longer provides a global `JSX`.
// Existing components in this project annotate return types as `JSX.Element`, so re-expose the
// global `JSX` namespace by aliasing it to `React.JSX`. This keeps those annotations valid without
// editing every component.
import type * as React from 'react';

declare global {
  namespace JSX {
    type Element = React.JSX.Element;
    type ElementClass = React.JSX.ElementClass;
    type ElementAttributesProperty = React.JSX.ElementAttributesProperty;
    type ElementChildrenAttribute = React.JSX.ElementChildrenAttribute;
    type LibraryManagedAttributes<C, P> = React.JSX.LibraryManagedAttributes<C, P>;
    type IntrinsicAttributes = React.JSX.IntrinsicAttributes;
    type IntrinsicClassAttributes<T> = React.JSX.IntrinsicClassAttributes<T>;
    type IntrinsicElements = React.JSX.IntrinsicElements;
  }
}
