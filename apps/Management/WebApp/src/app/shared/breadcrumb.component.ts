import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

export interface BreadcrumbItem {
  label: string;
  routerLink?: string[];
}

@Component({
  selector: 'app-breadcrumb',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <nav class="breadcrumb">
      @for (item of items(); let last = $last; track $index) {
        @if (!last) {
          <a [routerLink]="item.routerLink">{{ item.label }}</a>
          <span class="separator">›</span>
        } @else {
          <span class="current">{{ item.label }}</span>
        }
      }
    </nav>
  `,
  styles: [`
    .breadcrumb {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
      color: #666;
      margin-bottom: 20px;
    }
    a {
      color: #3b82f6;
      text-decoration: none;
    }
    a:hover {
      text-decoration: underline;
    }
    .separator {
      color: #ccc;
    }
    .current {
      color: #333;
      font-weight: 500;
    }
  `]
})
export class BreadcrumbComponent {
  items = input.required<BreadcrumbItem[]>();
}
