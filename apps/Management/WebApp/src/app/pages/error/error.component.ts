import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-error',
  standalone: true,
  templateUrl: './error.component.html',
  styleUrl: './error.component.scss'
})
export class ErrorComponent {
  message = inject(ActivatedRoute).snapshot.queryParamMap.get('message');

  retry() {
    window.location.assign('/');
  }
}
