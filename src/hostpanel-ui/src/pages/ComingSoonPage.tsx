import { Card, Empty, PageHead } from '../components/ui/primitives';
import type { IconName } from '../components/ui/Icons';

interface Props {
  title: string;
  description: string;
  icon?: IconName;
}

export function ComingSoonPage({ title, description, icon = 'info' }: Props) {
  return (
    <>
      <PageHead title={title} sub="Under construction" />
      <Card>
        <Empty
          icon={icon}
          title={`${title} is on the roadmap`}
          sub={description}
        />
      </Card>
    </>
  );
}
