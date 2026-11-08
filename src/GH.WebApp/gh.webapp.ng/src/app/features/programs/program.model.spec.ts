import { formatPrice } from './program.model';

describe('formatPrice', () => {
  it('shows free programs as Free', () => {
    expect(formatPrice({ model: 'Free', amountMinor: null, currency: null, interval: null })).toBe('Free');
  });

  it('converts minor units and adds the billing interval', () => {
    const text = formatPrice({ model: 'Recurring', amountMinor: 1999, currency: 'USD', interval: 'Yearly' });

    expect(text).toContain('19.99');
    expect(text).toContain('/ year');
  });
});
