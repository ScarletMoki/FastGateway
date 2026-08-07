import { memo } from 'react';
import { Stagger, StaggerItem } from '@/components/motion';

const NotFoundPage = memo(() => {
  return (
    <div className="flex flex-col items-center justify-center min-h-screen bg-background text-center px-4">
      <Stagger className="max-w-md w-full">
        <StaggerItem className="text-6xl font-bold text-muted-foreground mb-4">404</StaggerItem>
        <StaggerItem className="mb-2">
          <h1 className="text-2xl font-semibold text-foreground">页面不存在</h1>
        </StaggerItem>
        <StaggerItem className="text-muted-foreground mb-8">抱歉，您访问的页面不存在。</StaggerItem>
        <StaggerItem className="space-y-4">
          <button
            onClick={() => window.history.back()}
            className="inline-flex items-center justify-center rounded-md text-sm font-medium ring-offset-background transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50 border border-input bg-background hover:bg-accent hover:text-accent-foreground h-10 px-4 py-2 mr-2"
          >
            返回上页
          </button>
          <button
            onClick={() => window.location.href = '/dashboard'}
            className="inline-flex items-center justify-center rounded-md text-sm font-medium ring-offset-background transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50 bg-primary text-primary-foreground hover:bg-primary/90 h-10 px-4 py-2"
          >
            回到首页
          </button>
        </StaggerItem>
      </Stagger>
    </div>
  );
});

export default NotFoundPage;
