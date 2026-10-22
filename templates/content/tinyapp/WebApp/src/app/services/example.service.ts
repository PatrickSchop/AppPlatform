import { Injectable, inject } from '@angular/core';
import { API_CLIENT } from '@patrickschop/app-client-angular';
import type { ApiClient } from '@patrickschop/app-client';

/** One row of the example list. SampleApp's notes have a title, the template's items a name. */
export interface ExampleItem {
  id: string;
  title?: string;
  name?: string;
  createdUtc?: string;
}

/** The API route the example page lists. SampleApp serves `notes`; the tinyapp template `items`. */
const EXAMPLE_PATH = 'items';

/** The background task type the example page starts. SampleApp registers a `wordcount` handler. */
export const EXAMPLE_TASK_TYPE = 'wordcount';

@Injectable({ providedIn: 'root' })
export class ExampleService {
  // The platform's ApiClient adds the access token and the selected tenant to every request.
  private api = inject(API_CLIENT) as unknown as ApiClient;

  getItems(): Promise<ExampleItem[]> {
    return this.api.get(EXAMPLE_PATH);
  }
}
