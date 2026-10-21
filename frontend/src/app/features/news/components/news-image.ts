import { Component, input, linkedSignal } from '@angular/core';
@Component({
  selector: 'app-news-image',
  template: `
    @if (url() && !failed()) {
      <img
        [src]="url()"
        width="240"
        height="134"
        alt=""
        loading="lazy"
        draggable="false"
        referrerpolicy="no-referrer"
        (error)="failed.set(true)"
      />
    } @else {
      <div class="news-banner" aria-hidden="true">
        <span>F1</span><span class="banner-title" lang="en">{{ title() }}</span>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      overflow: hidden;
      border-radius: 8px;
      background: #17191d;
    }
    img {
      display: block;
      width: 100%;
      height: 100%;
      aspect-ratio: 240/134;
      object-fit: cover;
    }
    .news-banner {
      min-height: 134px;
      box-sizing: border-box;
      padding: 20px;
      display: flex;
      gap: 16px;
      align-items: center;
      color: #fff;
      background: linear-gradient(120deg, #b7192a 0%, #541722 40%, #17191d 100%);
    }
    .news-banner > span:first-child {
      font-size: 36px;
      font-weight: 900;
    }
    .banner-title {
      font-size: 14px;
      line-height: 1.6;
      overflow-wrap: anywhere;
    }
  `,
})
export class NewsImage {
  readonly url = input<string | null | undefined>();
  readonly title = input.required<string>();
  protected readonly failed = linkedSignal({ source: this.url, computation: () => false });
}
