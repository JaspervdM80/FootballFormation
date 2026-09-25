import { test, expect } from '../fixtures.js';

const paths = ['/', '/stats', '/no-such-page', '/_framework/blazor.web.js'];

for (const path of paths) {
  test(`${path} is sent with the anti-framing and nosniff headers`, async ({ request }) => {
    const response = await request.get(path);
    const headers = response.headers();

    expect(headers['content-security-policy']).toContain("frame-ancestors 'self'");
    expect(headers['content-security-policy']).toContain("object-src 'none'");
    expect(headers['x-frame-options']).toBe('SAMEORIGIN');
    expect(headers['x-content-type-options']).toBe('nosniff');
  });
}
