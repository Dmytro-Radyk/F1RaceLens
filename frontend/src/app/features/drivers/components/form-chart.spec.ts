import { TestBed } from '@angular/core/testing';
import { FormChart } from './form-chart';
import type { DriverRace } from '../models/driver-profile.types';

const race: DriverRace = {
  round: 1,
  name: 'British Grand Prix',
  date: '2025-07-06',
  circuit: 'Silverstone',
  result: {
    position: 1,
    positionText: '1',
    driverId: 'test',
    driverName: 'Test Driver',
    driverCode: null,
    team: null,
    grid: 2,
    laps: 50,
    time: null,
    status: 'Finished',
    points: 25,
    q1: null,
    q2: null,
    q3: null,
  },
};

describe('Form chart tooltips', () => {
  async function chart(races: readonly DriverRace[] = [race]) {
    await TestBed.configureTestingModule({ imports: [FormChart] }).compileComponents();
    const fixture = TestBed.createComponent(FormChart);
    fixture.componentRef.setInput('races', races);
    fixture.detectChanges();
    return fixture;
  }

  it('opens on hover and stays visible until the chart frame is left', async () => {
    const fixture = await chart();
    const point = fixture.nativeElement.querySelector('.chart-point') as HTMLElement;
    point.dispatchEvent(new Event('pointerenter'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]').textContent).toContain(
      'Етап 1 · British Grand Prix',
    );
    expect(fixture.nativeElement.querySelector('[role="tooltip"]').textContent).toContain(
      'Старт: 2 · Фініш: 1',
    );
    expect(fixture.nativeElement.querySelector('.chart-frame [role="tooltip"]')).not.toBeNull();
    fixture.nativeElement.querySelector('.chart-frame').dispatchEvent(new Event('pointerleave'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]')).toBeNull();
  });

  it('supports keyboard focus, Escape, click reopening and outside dismissal', async () => {
    const fixture = await chart();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    const tooltip = fixture.nativeElement.querySelector('[role="tooltip"]') as HTMLElement;
    expect(button.getAttribute('aria-describedby')).toBe(tooltip.id);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]')).toBeNull();
    button.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]')).not.toBeNull();
    document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]')).toBeNull();
  });

  it('explains retirements and ambiguous historical entries without a fake finish', async () => {
    const fixture = await chart([
      { ...race, result: { ...race.result, status: 'Engine', grid: 0 } },
    ]);
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]').textContent).toContain(
      'Схід: двигун',
    );
    expect(fixture.nativeElement.querySelector('[role="tooltip"]').textContent).not.toContain(
      'Фініш:',
    );
    fixture.componentRef.setInput('races', [race, race]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]')).toBeNull();
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="tooltip"]').textContent).toContain(
      'Кілька записів',
    );
  });

  it('combines coincident start and finish into one target', async () => {
    const fixture = await chart([{ ...race, result: { ...race.result, grid: 1 } }]);
    expect(fixture.nativeElement.querySelectorAll('button')).toHaveLength(1);
    expect(fixture.nativeElement.querySelector('button').getAttribute('aria-label')).toContain(
      'Старт і фініш',
    );
  });
});
