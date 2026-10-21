import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { InviteDialogComponent } from './invite-dialog.component';
import { ToastService } from '../../shared/toast.service';

describe('InviteDialogComponent', () => {
  let component: InviteDialogComponent;
  let fixture: ComponentFixture<InviteDialogComponent>;
  let toastService: ToastService;

  beforeEach(async () => {
    // Mock navigator.clipboard for tests
    Object.defineProperty(navigator, 'clipboard', {
      value: {
        writeText: vi.fn()
      },
      configurable: true
    });

    await TestBed.configureTestingModule({
      imports: [InviteDialogComponent],
      providers: [ToastService]
    }).compileComponents();

    fixture = TestBed.createComponent(InviteDialogComponent);
    component = fixture.componentInstance;
    toastService = TestBed.inject(ToastService);
    vi.spyOn(toastService, 'show');
  });

  it('should display token/URL when dialog opens', async () => {
    const testUrl = 'https://example.com/invite/token123';

    // Set initial inputs before first detectChanges
    component.url = testUrl;
    component.expiresUtc = '2025-12-31T23:59:59Z';

    // First detectChanges triggers ngOnInit
    fixture.detectChanges();
    await fixture.whenStable();

    // Second detectChanges ensures the signal value is reflected in the DOM
    fixture.detectChanges();

    const urlInput = fixture.nativeElement.querySelector('.url-input-wrapper input');
    expect(urlInput.value).toBe(testUrl);
  });

  it('should call navigator.clipboard.writeText when copy button is clicked', async () => {
    const testUrl = 'https://example.com/invite/token123';
    component.url = testUrl;

    (navigator.clipboard.writeText as any).mockResolvedValue(undefined);

    fixture.detectChanges();
    fixture.detectChanges();

    const copyButton = fixture.nativeElement.querySelector('.btn-copy');
    copyButton.click();

    await fixture.whenStable();

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(testUrl);
    expect(toastService.show).toHaveBeenCalledWith('Copied to clipboard', 'info');
  });

  it('should handle clipboard write errors', async () => {
    const testUrl = 'https://example.com/invite/token123';
    component.url = testUrl;

    (navigator.clipboard.writeText as any).mockRejectedValue(new Error('Copy failed'));

    fixture.detectChanges();
    fixture.detectChanges();

    const copyButton = fixture.nativeElement.querySelector('.btn-copy');
    copyButton.click();

    await fixture.whenStable();

    expect(toastService.show).toHaveBeenCalledWith('Failed to copy', 'error');
  });

  it('should emit close event when close button is clicked', () => {
    vi.spyOn(component.close, 'emit');
    component.url = 'https://example.com/invite/token123';

    fixture.detectChanges();

    const closeButton = fixture.nativeElement.querySelector('.dialog-actions button');
    closeButton.click();

    expect(component.close.emit).toHaveBeenCalled();
  });

  it('should emit close event when clicking outside dialog overlay', () => {
    vi.spyOn(component.close, 'emit');
    component.url = 'https://example.com/invite/token123';

    fixture.detectChanges();

    const overlay = fixture.nativeElement.querySelector('.dialog-overlay');
    overlay.click();

    expect(component.close.emit).toHaveBeenCalled();
  });

  it('should not emit close event when clicking inside dialog content', () => {
    vi.spyOn(component.close, 'emit');
    component.url = 'https://example.com/invite/token123';

    fixture.detectChanges();

    const content = fixture.nativeElement.querySelector('.dialog-content');
    content.click();

    expect(component.close.emit).not.toHaveBeenCalled();
  });
});
