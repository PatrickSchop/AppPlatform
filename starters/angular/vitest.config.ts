/// <reference types="vitest" />
import { defineConfig } from 'vitest/config';
import angular from '@analogjs/vite-plugin-angular';

export default defineConfig({
  plugins: [angular()],
  resolve: {
    // The platform libraries are linked from ../../../clients. Without these they would load
    // a second copy of Angular and their inject() calls could not see the app's injector.
    preserveSymlinks: true,
    dedupe: ['@angular/core', '@angular/common', '@angular/router', '@angular/platform-browser', 'rxjs'],
  },
  test: {
    globals: true,
    environment: 'jsdom',
    include: ['src/**/*.spec.ts'],
    setupFiles: ['src/test-setup.ts'],
    testTimeout: 10000
  }
});
